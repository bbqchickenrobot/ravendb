// -----------------------------------------------------------------------
//  <copyright file="PeriodicBackupTests.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Raven.Client.ServerWide.Operations;
using Raven.Client.ServerWide.PeriodicBackup;
using Xunit;

namespace FastTests.Server.Documents.PeriodicBackup
{
    public class PeriodicBackupTests : RavenTestBase
    {
        [Fact, Trait("Category", "Smuggler")]
        public async Task CanSetupPeriodicBackupWithVeryLargePeriods()
        {
            var backupPath = NewDataPath(suffix: "BackupFolder");
            using (var store = GetDocumentStore())
            {
                var config = new PeriodicBackupConfiguration
                {
                    LocalSettings = new LocalSettings
                    {
                        FolderPath = backupPath
                    },
                    FullBackupFrequency = "* */1 * * *",
                    IncrementalBackupFrequency = "* */2 * * *"
                };

                await store.Maintenance.Server.SendAsync(new UpdatePeriodicBackupOperation(config, store.Database));

                var periodicBackupRunner = (await GetDocumentDatabaseInstanceFor(store)).PeriodicBackupRunner;
                var backups = periodicBackupRunner.PeriodicBackups;
                Assert.Equal("* */1 * * *", backups.First().Configuration.FullBackupFrequency);
                Assert.Equal("* */2 * * *", backups.First().Configuration.IncrementalBackupFrequency);
            }
        }
    }
}
