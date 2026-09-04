using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class RuntimeSwingAdviserDbContextFactoryTests
{
    [Fact]
    public void CreateContext_EnablesForeignKeysAndWalJournalMode()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("swing-adviser-runtime-context-").FullName;

        try
        {
            long foreignKeysValue;
            string? journalModeValue;

            using (var context = RuntimeSwingAdviserDbContextFactory.CreateContext(tempDirectory))
            {
                var connection = context.Database.GetDbConnection();
                connection.Open();

                using var foreignKeysCommand = connection.CreateCommand();
                foreignKeysCommand.CommandText = "PRAGMA foreign_keys;";
                foreignKeysValue = Convert.ToInt64(foreignKeysCommand.ExecuteScalar());

                using var journalModeCommand = connection.CreateCommand();
                journalModeCommand.CommandText = "PRAGMA journal_mode;";
                journalModeValue = Convert.ToString(journalModeCommand.ExecuteScalar());
            }

            Assert.Equal(1L, foreignKeysValue);
            Assert.Equal("wal", journalModeValue, ignoreCase: true);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
