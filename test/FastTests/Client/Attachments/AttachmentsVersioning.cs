﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Raven.Client.Documents.Operations;
using Xunit;
using Raven.Client;
using Raven.Client.Documents.Session;
using Raven.Server.Documents;
using Raven.Server.Documents.Versioning;

namespace FastTests.Client.Attachments
{
    public class AttachmentsVersioning : RavenTestBase
    {
        [Fact]
        public void PutAttachments()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new VersioningConfiguration
                    {
                        Default = new VersioningConfigurationCollection
                        {
                            Active = true,
                            MaxRevisions = 5,
                        },
                        Collections = new Dictionary<string, VersioningConfigurationCollection>
                        {
                            ["Users"] = new VersioningConfigurationCollection
                            {
                                Active = true,
                                PurgeOnDelete = false,
                                MaxRevisions = 123
                            }
                        }
                    }, Constants.Documents.Versioning.ConfigurationKey);

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Fitzchak" }, "users/1");
                    session.SaveChanges();
                }

                var names = new[]
                {
                    "profile.png",
                    "background-photo.jpg",
                    "fileNAME_#$1^%_בעברית.txt"
                };
                using (var profileStream = new MemoryStream(new byte[] {1, 2, 3}))
                {
                    var result = store.Operations.Send(new PutAttachmentOperation("users/1", names[0], profileStream, "image/png"));
                    Assert.Equal(4, result.Etag);
                    Assert.Equal(names[0], result.Name);
                    Assert.Equal("users/1", result.DocumentId);
                    Assert.Equal("image/png", result.ContentType);
                    Assert.Equal("JCS/B3EIIB2gNVjsXTCD1aXlTgzuEz50", result.Hash);
                }
                using (var backgroundStream = new MemoryStream(new byte[] {10, 20, 30, 40, 50}))
                {
                    var result = store.Operations.Send(new PutAttachmentOperation("users/1", names[1], backgroundStream, "ImGgE/jPeG"));
                    Assert.Equal(8, result.Etag);
                    Assert.Equal(names[1], result.Name);
                    Assert.Equal("users/1", result.DocumentId);
                    Assert.Equal("ImGgE/jPeG", result.ContentType);
                    Assert.Equal("mpqSy7Ky+qPhkBwhLiiM2no82Wvo9gQw", result.Hash);
                }
                using (var fileStream = new MemoryStream(new byte[] {1, 2, 3, 4, 5}))
                {
                    var result = store.Operations.Send(new PutAttachmentOperation("users/1", names[2], fileStream, null));
                    Assert.Equal(13, result.Etag);
                    Assert.Equal(names[2], result.Name);
                    Assert.Equal("users/1", result.DocumentId);
                    Assert.Equal("", result.ContentType);
                    Assert.Equal("PN5EZXRY470m7BLxu9MsOi/WwIRIq4WN", result.Hash);
                }
                var statistics = store.Admin.Send(new GetStatisticsOperation());
                Assert.Equal(3, statistics.CountOfAttachments);
                Assert.Equal(4, statistics.CountOfRevisionDocuments.Value);
                Assert.Equal(2, statistics.CountOfDocuments);
                Assert.Equal(0, statistics.CountOfIndexes);

                using (var session = store.OpenSession())
                {
                    var revisions = session.Advanced.GetRevisionsFor<User>("users/1");
                    Assert.Equal(4, revisions.Count);
                    var metadata1 = session.Advanced.GetMetadataFor(revisions[0]);
                    Assert.Equal((DocumentFlags.Versioned | DocumentFlags.FromVersionStorage).ToString(), metadata1[Constants.Documents.Metadata.Flags]);
                    Assert.False(metadata1.ContainsKey(Constants.Documents.Metadata.Attachments));

                    AssertRevisionAttachments(names, 1, revisions[1], session);
                    AssertRevisionAttachments(names, 2, revisions[2], session);
                    AssertRevisionAttachments(names, 3, revisions[3], session);
                }

                // Delete document should delete all the attachments
                store.Commands().Delete("users/1", null);
                statistics = store.Admin.Send(new GetStatisticsOperation());
                Assert.Equal(3, statistics.CountOfAttachments);
                Assert.Equal(4, statistics.CountOfRevisionDocuments.Value);
            }
        }

        private void AssertRevisionAttachments(string[] names, int expectedCount, User revision, IDocumentSession session)
        {
            var metadata = session.Advanced.GetMetadataFor(revision);
            Assert.Equal((DocumentFlags.Versioned | DocumentFlags.FromVersionStorage | DocumentFlags.HasAttachments).ToString(), metadata[Constants.Documents.Metadata.Flags]);
            var attachments = metadata.GetObjects(Constants.Documents.Metadata.Attachments);
            Assert.Equal(expectedCount, attachments.Length);

            var orderedNames = names.Take(expectedCount).OrderBy(x => x).ToArray();
            for (var i = 0; i < expectedCount; i++)
            {
                var name = orderedNames[i];
                var attachment = attachments[i];
                Assert.Equal(name, attachment.GetString(nameof(Attachment.Name)));
                var hash = attachment.GetString(nameof(AttachmentResult.Hash));
                if (name == names[1])
                {
                    Assert.Equal("mpqSy7Ky+qPhkBwhLiiM2no82Wvo9gQw", hash);
                }
                else if (name == names[2])
                {
                    Assert.Equal("PN5EZXRY470m7BLxu9MsOi/WwIRIq4WN", hash);
                }
                else if (name == names[0])
                {
                    Assert.Equal("JCS/B3EIIB2gNVjsXTCD1aXlTgzuEz50", hash);
                }
            }

            var changeVector = session.Advanced.GetChangeVectorFor(revision);
            var readBuffer = new byte[8];
            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i];
                using (var attachmentStream = new MemoryStream(readBuffer))
                {
                    var attachment = session.Advanced.GetRevisionAttachment("users/1", name, changeVector, (result, stream) => stream.CopyTo(attachmentStream));
                    if (i >= expectedCount)
                    {
                        Assert.Null(attachment);
                        continue;
                    }

                    Assert.Equal(name, attachment.Name);
                    if (name == names[0])
                    {
                        if (expectedCount == 1)
                            Assert.Equal(7, attachment.Etag);
                        else if (expectedCount == 2)
                            Assert.Equal(12, attachment.Etag);
                        else if (expectedCount == 3)
                            Assert.Equal(18, attachment.Etag);
                        else
                            throw new ArgumentOutOfRangeException(nameof(i));
                        Assert.Equal(new byte[] {1, 2, 3}, readBuffer.Take(3));
                        Assert.Equal("image/png", attachment.ContentType);
                        Assert.Equal(3, attachmentStream.Position);
                        Assert.Equal("JCS/B3EIIB2gNVjsXTCD1aXlTgzuEz50", attachment.Hash);
                    }
                    else if (name == names[1])
                    {
                        if (expectedCount == 2)
                            Assert.Equal(11, attachment.Etag);
                        else if (expectedCount == 3)
                            Assert.Equal(17, attachment.Etag);
                        else
                            throw new ArgumentOutOfRangeException(nameof(i));
                        Assert.Equal(new byte[] {10, 20, 30, 40, 50}, readBuffer.Take(5));
                        Assert.Equal("ImGgE/jPeG", attachment.ContentType);
                        Assert.Equal(5, attachmentStream.Position);
                        Assert.Equal("mpqSy7Ky+qPhkBwhLiiM2no82Wvo9gQw", attachment.Hash);
                    }
                    else if (name == names[2])
                    {
                        if (expectedCount == 3)
                            Assert.Equal(16, attachment.Etag);
                        else
                            throw new ArgumentOutOfRangeException(nameof(i));
                        Assert.Equal(new byte[] {1, 2, 3, 4, 5}, readBuffer.Take(5));
                        Assert.Equal("", attachment.ContentType);
                        Assert.Equal(5, attachmentStream.Position);
                        Assert.Equal("PN5EZXRY470m7BLxu9MsOi/WwIRIq4WN", attachment.Hash);
                    }
                }
            }
        }

        private class User
        {
            public string Name { get; set; }
        }
    }
}