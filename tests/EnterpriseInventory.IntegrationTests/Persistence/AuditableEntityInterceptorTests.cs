using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Persistence;

public class AuditableEntityInterceptorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Saving_without_a_signed_in_user_is_refused_before_the_database_is_reached(string? userName)
    {
        // The interceptor runs before EF Core opens a connection, so no SQL Server is needed here.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Connect Timeout=1")
            .AddInterceptors(new AuditableEntityInterceptor(new TestCurrentUser(userName), TimeProvider.System))
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.Cities.Add(City.Create("Ankara"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("signed-in user", exception.Message, StringComparison.Ordinal);
    }
}
