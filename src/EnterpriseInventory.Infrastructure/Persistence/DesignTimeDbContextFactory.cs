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
    private const string NoDatabase = "Server=.;Database=EnterpriseInventory;Integrated Security=true;TrustServerCertificate=false";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Migrations");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(string.IsNullOrWhiteSpace(connectionString) ? NoDatabase : connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
