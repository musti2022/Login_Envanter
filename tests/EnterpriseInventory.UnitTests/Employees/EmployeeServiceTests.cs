using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Employees;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.UnitTests.Employees;

public class EmployeeServiceTests
{
    private readonly ScriptedDirectory _directory = new();

    [Theory]
    [InlineData("me")]
    [InlineData("  mehmet  ")]
    [InlineData("mehmet öztürk")]
    [InlineData("a b c d")]
    public async Task A_search_of_two_to_sixty_four_characters_and_up_to_four_words_is_sent_to_the_directory(string q)
    {
        var result = await Service().SearchAsync(new EmployeeSearchRequest { Q = q }, CancellationToken.None);

        Assert.NotNull(result.Response);
        Assert.Equal(string.Join(' ', q.Split(' ', StringSplitOptions.RemoveEmptyEntries)), _directory.LastTerm);
        Assert.Equal(EmployeeService.MaxResults, _directory.LastLimit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("m")]
    [InlineData(" m ")]
    [InlineData("a b c d e")]
    [InlineData("mehmet\u0000")]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345")]
    public async Task Other_searches_are_refused_without_asking_the_directory(string? q)
    {
        var result = await Service().SearchAsync(new EmployeeSearchRequest { Q = q }, CancellationToken.None);

        Assert.True(result.Errors!.ContainsKey(nameof(EmployeeSearchRequest.Q)));
        Assert.Null(_directory.LastTerm);
    }

    [Fact]
    public async Task People_are_listed_by_name_in_Turkish_order_without_disabled_accounts()
    {
        _directory.People = [Person("zeynep", "Zeynep Ak"), Person("cagla", "Çağla Er"), Person("pasif", "Pasif Kişi", enabled: false), Person("can", "Can Su")];
        _directory.HasMore = true;

        var result = await Service().SearchAsync(new EmployeeSearchRequest { Q = "ab" }, CancellationToken.None);

        Assert.Equal(["can", "cagla", "zeynep"], result.Response!.Items.Select(i => i.UserName));
        Assert.True(result.Response.HasMore);
    }

    [Fact]
    public async Task An_unavailable_directory_is_reported_as_such()
    {
        _directory.Status = DirectoryLookupStatus.DirectoryUnavailable;

        var result = await Service().SearchAsync(new EmployeeSearchRequest { Q = "mehmet" }, CancellationToken.None);

        Assert.True(result.DirectoryUnavailable);
    }

    private EmployeeService Service() =>
        new(new ServiceCollection().AddApplication().BuildServiceProvider().GetRequiredService<IValidator<EmployeeSearchRequest>>(), _directory);

    private static DirectoryPerson Person(string userName, string displayName, bool enabled = true) =>
        new(Guid.NewGuid(), userName, displayName, null, null, null, enabled);

    private sealed class ScriptedDirectory : IEmployeeDirectory
    {
        public IReadOnlyList<DirectoryPerson> People { get; set; } = [];

        public bool HasMore { get; set; }

        public DirectoryLookupStatus Status { get; set; } = DirectoryLookupStatus.Succeeded;

        public string? LastTerm { get; private set; }

        public int LastLimit { get; private set; }

        public Task<DirectoryPeopleResult> SearchAsync(string term, int limit, CancellationToken cancellationToken)
        {
            (LastTerm, LastLimit) = (term, limit);
            return Task.FromResult(Status == DirectoryLookupStatus.Succeeded ? new DirectoryPeopleResult(Status, People, HasMore) : DirectoryPeopleResult.Unavailable);
        }

        public Task<DirectoryPersonResult> FindAsync(Guid objectGuid, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
