using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Employees;
using EnterpriseInventory.Domain.Common;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseInventory.UnitTests.Assets;

/// <summary>
/// The assignment use case before the database: input rules, and the employee read again from the directory so
/// only someone who exists and is enabled now gets the asset.
/// </summary>
public class AssetAssignmentServiceTests
{
    private const string RowVersion = "AAAAAAAAB9E=";

    private static readonly DirectoryPerson Mehmet = new(Guid.NewGuid(), "mehmet.user", "Mehmet Öztürk", null, "Muhasebe", null, IsEnabled: true);

    private readonly ScriptedDirectory _directory = new();
    private readonly RecordingStore _store = new();

    [Fact]
    public async Task The_employee_as_the_directory_says_now_and_the_trimmed_texts_go_to_the_store()
    {
        _directory.Person = DirectoryPersonResult.Found(Mehmet);

        var result = await Service().AssignAsync(7, Request() with { AssignmentDescription = "  Dizüstü  ", Notes = "   " }, CancellationToken.None);

        Assert.Equal(AssetWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal((7, new AssignmentDraft(Mehmet, "Dizüstü", null)), _store.Assigned);
        Assert.Equal(Mehmet.ObjectGuid, _directory.AskedFor);
    }

    [Fact]
    public async Task An_invalid_request_never_reaches_the_directory()
    {
        var result = await Service().AssignAsync(7, Request() with { AssignmentDescription = null, RowVersion = "x" }, CancellationToken.None);

        Assert.Equal(AssetWriteOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(nameof(AssignAssetRequest.AssignmentDescription), result.Errors!.Keys);
        Assert.Contains(nameof(AssignAssetRequest.RowVersion), result.Errors.Keys);
        Assert.Null(_directory.AskedFor);
        Assert.Null(_store.Assigned);
    }

    [Fact]
    public async Task Someone_the_directory_does_not_know_is_a_field_error()
    {
        _directory.Person = DirectoryPersonResult.NotFound;

        var result = await Service().AssignAsync(7, Request(), CancellationToken.None);

        Assert.Equal([AssetAssignmentService.EmployeeNotFound], result.Errors![nameof(AssignAssetRequest.EmployeeObjectGuid)]);
        Assert.Null(_store.Assigned);
    }

    [Fact]
    public async Task A_disabled_account_is_refused_by_rule_and_an_unreachable_directory_writes_nothing()
    {
        _directory.Person = DirectoryPersonResult.Found(Mehmet with { IsEnabled = false });
        Assert.Equal(DomainErrors.Employee.Inactive, (await Service().AssignAsync(7, Request(), CancellationToken.None)).RuleCode);

        _directory.Person = DirectoryPersonResult.Unavailable;
        Assert.Equal(AssetWriteOutcome.DirectoryUnavailable, (await Service().AssignAsync(7, Request(), CancellationToken.None)).Outcome);
        Assert.Null(_store.Assigned);
    }

    private static AssignAssetRequest Request() => new()
    {
        EmployeeObjectGuid = Mehmet.ObjectGuid,
        AssignmentDescription = "Dizüstü",
        RowVersion = RowVersion,
    };

    private AssetAssignmentService Service()
    {
        var services = new ServiceCollection().AddApplication().BuildServiceProvider();
        return new AssetAssignmentService(
            services.GetRequiredService<IValidator<AssignAssetRequest>>(),
            services.GetRequiredService<IValidator<ReturnAssetRequest>>(),
            services.GetRequiredService<IValidator<AssetAssignmentsRequest>>(),
            _directory,
            _store,
            new AssetChangePublisher(new NoAssetChangeNotifier(), NullLogger<AssetChangePublisher>.Instance));
    }

    private sealed class ScriptedDirectory : IEmployeeDirectory
    {
        public DirectoryPersonResult Person { get; set; } = DirectoryPersonResult.NotFound;

        public Guid? AskedFor { get; private set; }

        public Task<DirectoryPeopleResult> SearchAsync(string term, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DirectoryPersonResult> FindAsync(Guid objectGuid, CancellationToken cancellationToken)
        {
            AskedFor = objectGuid;
            return Task.FromResult(Person);
        }
    }

    private sealed class RecordingStore : IAssetAssignmentStore
    {
        public (int AssetId, AssignmentDraft Draft)? Assigned { get; private set; }

        public Task<AssetWriteResult> AssignAsync(int assetId, AssignmentDraft draft, byte[] rowVersion, CancellationToken cancellationToken)
        {
            Assigned = (assetId, draft);
            return Task.FromResult(AssetWriteResult.Succeeded(null));
        }

        public Task<AssetWriteResult> ReturnAsync(int assetId, byte[] rowVersion, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PagedResult<AssetAssignmentItem>?> ListAsync(int assetId, int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
