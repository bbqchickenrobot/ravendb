// -----------------------------------------------------------------------
//  <copyright file="SmugglerHelper.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.IO;
using System.IO.Compression;

using Raven.Abstractions.Connection;
using Raven.Abstractions.Data;
using Raven.Abstractions.Json;
using Raven.Imports.Newtonsoft.Json;
using Raven.Json.Linq;

namespace Raven.Abstractions.Database.Smuggler
{
    public static class SmugglerHelper
    {
        public static RavenJToken HandleConflictDocuments(RavenJObject metadata)
        {
            if (metadata == null)
                return null;

            if (metadata.ContainsKey(Constants.RavenReplicationConflictDocument))
                metadata[Constants.RavenReplicationConflictDocumentForcePut] = true;

            if (metadata.ContainsKey(Constants.RavenReplicationConflict))
                metadata[Constants.RavenReplicationConflictSkipResolution] = true;

            return metadata;
        }

        public static RavenJToken DisableVersioning(RavenJObject metadata)
        {
            if (metadata != null)
                metadata[Constants.RavenIgnoreVersioning] = true;

            return metadata;
        }

        public static bool TryGetJsonReaderForStream(Stream stream, out JsonTextReader jsonTextReader,out CountingStream sizeStream)
        {
            jsonTextReader = null;
            sizeStream = null;
            try
            {
                stream.Position = 0;
                sizeStream = new CountingStream(new GZipStream(stream, CompressionMode.Decompress));
                var streamReader = new StreamReader(sizeStream);

                jsonTextReader = new RavenJsonTextReader(streamReader);

                if (jsonTextReader.Read() == false)
                    return false;
            }
            catch (Exception e)
            {
                if (e is InvalidDataException == false)
                {
                    if(sizeStream != null)
                        sizeStream.Dispose();
                    throw;
                }

                stream.Seek(0, SeekOrigin.Begin);
                sizeStream = new CountingStream(stream);

                var streamReader = new StreamReader(sizeStream);
                jsonTextReader = new JsonTextReader(streamReader);

                if (jsonTextReader.Read() == false)
                    return false;
            }

            return true;
        }

        public static RavenJToken StripReplicationInformationFromMetadata(RavenJObject metadata)
        {
            if (metadata != null)
            {
                metadata.Remove(Constants.RavenReplicationHistory);
                metadata.Remove(Constants.RavenReplicationSource);
                metadata.Remove(Constants.RavenReplicationVersion);
            }

            return metadata;
        }
    }
}
