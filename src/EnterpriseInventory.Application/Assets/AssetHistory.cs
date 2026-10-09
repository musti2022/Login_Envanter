using System.Text.Json;
using EnterpriseInventory.Domain.Auditing;
using FluentValidation;

namespace EnterpriseInventory.Application.Assets;

/// <summary>Query string of <c>GET /api/assets/{id}/history</c>.</summary>
public sealed record AssetHistoryRequest
{
    public const int DefaultPageSize = 50;

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

/// <summary>
/// One audit record of an asset: who did what, when, and the fields it changed (old and new values as JSON
/// objects; <c>null</c> where there were none, as in the old values of <see cref="AuditAction.Created"/>).
/// </summary>
public sealed record AssetHistoryEntry(
    long Id,
    AuditAction Action,
    string UserName,
    DateTimeOffset Timestamp,
    string CorrelationId,
    JsonElement? OldValues,
    JsonElement? NewValues);

/// <summary>A page of an asset's history, the reasons the request was refused, or neither when there is no such asset.</summary>
public sealed record AssetHistoryResult(PagedResult<AssetHistoryEntry>? Page, IDictionary<string, string[]>? Errors)
{
    public bool AssetNotFound => Page is null && Errors is null;
}

internal sealed class AssetHistoryRequestValidator : AbstractValidator<AssetHistoryRequest>
{
    public AssetHistoryRequestValidator()
    {
        RuleFor(r => r.Page).MustBePage();
        RuleFor(r => r.PageSize).MustBePageSize();
    }
}

/// <summary>
/// Query string of <c>DELETE /api/assets/{id}</c>, which archives the asset: the <c>rowVersion</c> the caller read,
/// URL-encoded (base64 has <c>+</c>, <c>/</c> and <c>=</c>).
/// </summary>
public sealed record ArchiveAssetRequest
{
    public string? RowVersion { get; init; }
}

internal sealed class ArchiveAssetRequestValidator : AbstractValidator<ArchiveAssetRequest>
{
    public ArchiveAssetRequestValidator()
    {
        RuleFor(r => r.RowVersion).MustBeRowVersion();
    }
}
