using System.Globalization;
using EnterpriseInventory.Domain.Auditing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnterpriseInventory.IntegrationTests.Persistence;

/// <summary>
/// Day 39: the committed backup and restore scripts (<c>deploy/sql</c>) on the real SQL Server, run the way sqlcmd runs
/// them: a backup taken before a migration, the migration, new writes, and the restore that undoes all of it.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class DatabaseRecoveryTests(SqlServerDatabaseFixture database) : IAsyncLifetime
{
    private readonly List<string> _backupFiles = [];

    private static string Master => new SqlConnectionStringBuilder(SqlServerDatabaseFixture.ServerConnectionString) { InitialCatalog = "master" }.ConnectionString;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // The files are on the test server; xp_delete_file removes nothing but SQL Server backup files.
        foreach (var file in _backupFiles)
        {
            await QueryAsync(Master, "EXEC master.dbo.xp_delete_file 0, @file", ("@file", file));
        }
    }

    [SqlServerFact]
    public async Task Restoring_the_backup_taken_before_a_migration_brings_back_its_schema_and_data()
    {
        var connectionString = await database.CreateEmptyDatabaseAsync();
        var name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        List<string> migrations;
        await using (var context = database.CreateContextFor(connectionString))
        {
            migrations = [.. context.Database.GetMigrations()];
            await context.GetService<IMigrator>().MigrateAsync(migrations[^2]);
            context.Add(PersistenceTestData.NewAsset("DMR-YEDEK-1"));
            context.AuditLogs.Add(AuditLog.Create("Asset", "1", AuditAction.Created, null, "{}", "ayse.admin", database.Clock.GetUtcNow(), "corr-1"));
            await context.SaveChangesAsync();
        }

        var before = await AssetRowsAsync(connectionString);
        var backupFile = await BackupFileAsync(name);

        await SqlcmdScript.RunAsync(Master, "deploy/sql/backup-before-migration.sql", Variables(name, backupFile));
        await SqlcmdScript.RunAsync(connectionString, DeploymentScript.RelativePath);
        await using (var context = database.CreateContextFor(connectionString))
        {
            Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
            context.Add(PersistenceTestData.NewAsset("DMR-YEDEK-2"));
            await context.SaveChangesAsync();
        }

        await QueryAsync(connectionString, "UPDATE dbo.Assets SET Description = N'Yedekten sonra' WHERE AssetCode = N'DMR-YEDEK-1'");
        await SqlcmdScript.RunAsync(Master, "deploy/sql/restore-from-backup.sql", Variables(name, backupFile));
        // The restore closed every connection to the database; pooled ones are gone.
        SqlConnection.ClearAllPools();

        Assert.Equal(before, await AssetRowsAsync(connectionString));
        Assert.Equal(["1"], await QueryAsync(connectionString, "SELECT COUNT(*) FROM dbo.AuditLogs"));
        Assert.Equal(["ONLINE MULTI_USER"], await QueryAsync(Master, "SELECT CONCAT(state_desc, ' ', user_access_desc) FROM sys.databases WHERE name = @name", ("@name", name)));
        await using (var context = database.CreateContextFor(connectionString))
        {
            Assert.Equal(migrations.Take(migrations.Count - 1), await context.Database.GetAppliedMigrationsAsync());
        }

        // After the rollback the same deployment can be tried again.
        await SqlcmdScript.RunAsync(connectionString, DeploymentScript.RelativePath);
        await using (var context = database.CreateContextFor(connectionString))
        {
            Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
        }
    }

    [SqlServerFact]
    public async Task A_backup_of_another_database_or_a_missing_file_is_refused_before_anyone_is_disconnected()
    {
        var other = await database.CreateMigratedDatabaseAsync();
        var otherName = new SqlConnectionStringBuilder(other).InitialCatalog;
        var target = await database.CreateMigratedDatabaseAsync();
        var targetName = new SqlConnectionStringBuilder(target).InitialCatalog;
        await using (var context = database.CreateContextFor(target))
        {
            context.Add(PersistenceTestData.NewAsset("DMR-KALIR"));
            await context.SaveChangesAsync();
        }

        var otherBackup = await BackupFileAsync(otherName);
        await SqlcmdScript.RunAsync(Master, "deploy/sql/backup-before-migration.sql", Variables(otherName, otherBackup));
        var missing = otherBackup.Replace(".bak", "-yok.bak", StringComparison.Ordinal);

        foreach (var file in new[] { otherBackup, missing })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => SqlcmdScript.RunAsync(Master, "deploy/sql/restore-from-backup.sql", Variables(targetName, file)));
            Assert.Contains("geri yükleme yapılmadı", error.Message, StringComparison.Ordinal);
        }

        Assert.Equal(["ONLINE MULTI_USER"], await QueryAsync(Master, "SELECT CONCAT(state_desc, ' ', user_access_desc) FROM sys.databases WHERE name = @name", ("@name", targetName)));
        Assert.Equal(["DMR-KALIR"], await QueryAsync(target, "SELECT AssetCode FROM dbo.Assets"));
    }

    private static Dictionary<string, string> Variables(string databaseName, string backupFile) =>
        new() { ["DatabaseName"] = databaseName, ["BackupFile"] = backupFile };

    /// <summary>A file in the server's default backup folder, removed when the tests finish.</summary>
    private async Task<string> BackupFileAsync(string databaseName)
    {
        var folder = (await QueryAsync(Master, "SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath'))")).Single();
        var separator = folder.Contains('\\', StringComparison.Ordinal) ? "\\" : "/";
        var file = $"{folder.TrimEnd('\\', '/')}{separator}{databaseName}.bak";
        _backupFiles.Add(file);
        return file;
    }

    /// <summary>Every asset with its description and row version, as the raw table holds them.</summary>
    private static Task<List<string>> AssetRowsAsync(string connectionString) =>
        QueryAsync(connectionString, "SELECT CONCAT(AssetCode, ' | ', Description, ' | ', CONVERT(varchar(20), RowVersion, 1)) FROM dbo.Assets ORDER BY AssetCode");

    private static async Task<List<string>> QueryAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constant test SQL; values are parameters.
        command.CommandText = sql;
#pragma warning restore CA2100
        foreach (var (parameterName, value) in parameters)
        {
            command.Parameters.AddWithValue(parameterName, value);
        }

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return rows;
    }
}
