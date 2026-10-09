using EnterpriseInventory.Application.Employees;
using EnterpriseInventory.Domain.Assets;
using FluentValidation;

namespace EnterpriseInventory.Application.Assets;

/// <summary>Body of <c>POST /api/assets/{id}/assignments</c>: who gets the asset, what was handed over, and the version read.</summary>
public sealed record AssignAssetRequest
{
    /// <summary>The employee's AD objectGUID, as the employee search returned it.</summary>
    public Guid? EmployeeObjectGuid { get; init; }

    /// <summary>"Zimmet Tanımı": what was handed over, e.g. "Dizüstü bilgisayar + şarj adaptörü".</summary>
    public string? AssignmentDescription { get; init; }

    public string? Notes { get; init; }

    /// <summary>The asset's <c>rowVersion</c> from the caller's last read; a stale one is refused with 409.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>Body of <c>POST /api/assets/{id}/returns</c>: the asset's version the caller read.</summary>
public sealed record ReturnAssetRequest
{
    public string? RowVersion { get; init; }
}

/// <summary>Query string of <c>GET /api/assets/{id}/assignments</c>.</summary>
public sealed record AssetAssignmentsRequest
{
    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

/// <summary>
/// A validated assignment: the employee as the directory describes them now (not as the caller sent them),
/// and the handover texts.
/// </summary>
public sealed record AssignmentDraft(DirectoryPerson Employee, string AssignmentDescription, string? Notes);

/// <summary>One period during which an employee held the asset; <see cref="ReturnedAt"/> is empty while it lasts.</summary>
public sealed record AssetAssignmentItem(
    int Id,
    int EmployeeId,
    string UserName,
    string DisplayName,
    string? Department,
    string? AssignmentDescription,
    string? Notes,
    DateTimeOffset AssignedAt,
    string AssignedBy,
    DateTimeOffset? ReturnedAt,
    string? ReturnedBy);

/// <summary>A page of an asset's assignments, the reasons the request was refused, or neither when there is no such asset.</summary>
public sealed record AssetAssignmentsResult(PagedResult<AssetAssignmentItem>? Page, IDictionary<string, string[]>? Errors)
{
    public bool AssetNotFound => Page is null && Errors is null;
}

/// <summary>Writes and reads assignments. Callers pass input that <see cref="AssetAssignmentService"/> has validated.</summary>
public interface IAssetAssignmentStore
{
    /// <summary>
    /// Gives the asset to the employee: refreshes (or adds) the employee's record from the directory, opens the
    /// assignment, marks the asset assigned and writes an <c>Assigned</c> audit record, all in one transaction.
    /// Refused with <see cref="AssetWriteOutcome.ConcurrencyConflict"/> when the asset is no longer at
    /// <paramref name="rowVersion"/>; if another assignment wins a race, the database's one-active-assignment index
    /// refuses this one (<see cref="Domain.Common.DomainErrors.Asset.AlreadyAssigned"/>).
    /// </summary>
    Task<AssetWriteResult> AssignAsync(int assetId, AssignmentDraft draft, byte[] rowVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Closes the active assignment (it stays as history), makes the asset available again and writes a
    /// <c>Returned</c> audit record, in one transaction. Same version check as <see cref="AssignAsync"/>.
    /// </summary>
    Task<AssetWriteResult> ReturnAsync(int assetId, byte[] rowVersion, CancellationToken cancellationToken);

    /// <summary>The asset's assignments, newest first, or <c>null</c> when there is no asset with that ID (archived ones count).</summary>
    Task<PagedResult<AssetAssignmentItem>?> ListAsync(int assetId, int page, int pageSize, CancellationToken cancellationToken);
}

internal sealed class AssignAssetRequestValidator : AbstractValidator<AssignAssetRequest>
{
    public AssignAssetRequestValidator()
    {
        RuleFor(r => r.EmployeeObjectGuid)
            .Must(guid => guid is { } value && value != Guid.Empty)
            .WithMessage("Zimmetlenecek çalışanı seçin.");

        RuleFor(r => r.AssignmentDescription)
            .Cascade(CascadeMode.Stop)
            .Must(text => !string.IsNullOrWhiteSpace(text)).WithMessage("Zimmet tanımı zorunludur.")
            .Must(text => text!.Trim().Length <= AssetAssignment.AssignmentDescriptionMaxLength)
            .WithMessage($"Zimmet tanımı en fazla {AssetAssignment.AssignmentDescriptionMaxLength} karakter olabilir.");

        RuleFor(r => r.Notes)
            .Must(text => (text?.Trim().Length ?? 0) <= AssetAssignment.NotesMaxLength)
            .WithMessage($"Not en fazla {AssetAssignment.NotesMaxLength} karakter olabilir.");

        RuleFor(r => r.RowVersion).MustBeRowVersion();
    }
}

internal sealed class ReturnAssetRequestValidator : AbstractValidator<ReturnAssetRequest>
{
    public ReturnAssetRequestValidator()
    {
        RuleFor(r => r.RowVersion).MustBeRowVersion();
    }
}

internal sealed class AssetAssignmentsRequestValidator : AbstractValidator<AssetAssignmentsRequest>
{
    public AssetAssignmentsRequestValidator()
    {
        RuleFor(r => r.Page).MustBePage();
        RuleFor(r => r.PageSize).MustBePageSize();
    }
}

/// <summary>
/// Assignment use cases. The employee is read again from the directory by objectGUID before anything is written,
/// so an asset is only given to someone who exists and whose account is enabled now; directory access needs no
/// open transaction.
/// </summary>
public sealed class AssetAssignmentService(
    IValidator<AssignAssetRequest> assignValidator,
    IValidator<ReturnAssetRequest> returnValidator,
    IValidator<AssetAssignmentsRequest> listValidator,
    IEmployeeDirectory directory,
    IAssetAssignmentStore store)
{
    public const int DefaultPageSize = 50;

    /// <summary>Field error when the chosen person is no longer in the directory.</summary>
    public const string EmployeeNotFound = "Seçilen çalışan Active Directory'de bulunamadı; aramayı yenileyip tekrar seçin.";

    public async Task<AssetWriteResult> AssignAsync(int assetId, AssignAssetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await assignValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return AssetWriteResult.Invalid(validation.ToDictionary());
        }

        var found = await directory.FindAsync(request.EmployeeObjectGuid!.Value, cancellationToken).ConfigureAwait(false);
        switch (found.Status)
        {
            case DirectoryLookupStatus.NotFound:
                return AssetWriteResult.Invalid(new Dictionary<string, string[]> { [nameof(AssignAssetRequest.EmployeeObjectGuid)] = [EmployeeNotFound] });
            case DirectoryLookupStatus.DirectoryUnavailable:
                return AssetWriteResult.DirectoryUnavailable;
        }

        if (!found.Person!.IsEnabled)
        {
            return AssetWriteResult.Rule(Domain.Common.DomainErrors.Employee.Inactive);
        }

        var draft = new AssignmentDraft(found.Person, request.AssignmentDescription!.Trim(), string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim());
        return await store.AssignAsync(assetId, draft, AssetRowVersion.Decode(request.RowVersion), cancellationToken).ConfigureAwait(false);
    }

    public async Task<AssetWriteResult> ReturnAsync(int assetId, ReturnAssetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await returnValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return validation.IsValid
            ? await store.ReturnAsync(assetId, AssetRowVersion.Decode(request.RowVersion), cancellationToken).ConfigureAwait(false)
            : AssetWriteResult.Invalid(validation.ToDictionary());
    }

    public async Task<AssetAssignmentsResult> ListAsync(int assetId, AssetAssignmentsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await listValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new AssetAssignmentsResult(null, validation.ToDictionary());
        }

        var page = await store.ListAsync(assetId, request.Page ?? 1, request.PageSize ?? DefaultPageSize, cancellationToken).ConfigureAwait(false);
        return new AssetAssignmentsResult(page, null);
    }
}
