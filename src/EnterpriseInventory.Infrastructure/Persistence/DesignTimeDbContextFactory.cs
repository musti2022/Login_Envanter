using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnterpriseInventory.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet-ef tool. The connection string comes from the
/// <c>ConnectionStrings__Migrations</c> environment variable (an account allowed to change the schema);
/// creating a migration or a SQL script does not need a database.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    // Deliberately unreachable: without the variable, commands that need a database fail instead of
    // migrating whatever database happens to be on the local default instance.
    private const string NoDatabase = "Server=ConnectionStrings__Migrations-is-not-set;Database=none;Connect Timeout=1";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Migrations");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(string.IsNullOrWhiteSpace(connectionString) ? NoDatabase : connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
