using EnterpriseInventory.Domain.Assets;

namespace EnterpriseInventory.Application.Assets;

/// <summary>A lookup value (brand, model, city, department, location) as shown with an asset.</summary>
public sealed record NamedReference(int Id, string Name);

/// <summary>One page of a list, with what the caller needs to show page links.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>One row of the inventory table: the columns the users asked for, flattened to names.</summary>
/// <param name="AssignedUserName">The holder's directory user name, when the asset is assigned.</param>
/// <param name="AssignmentDescription">"Zimmet Tanımı" of the active assignment.</param>
public sealed record AssetListItem(
    int Id,
    string AssetCode,
    string? ComputerName,
    string BrandName,
    string ModelName,
    string? SerialNumber,
    AssetType AssetType,
    AssetStatus Status,
    string CityName,
    string DepartmentName,
    string? LocationName,
    string? AssignedUserName,
    string? AssignedDisplayName,
    string? AssignmentDescription,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Everything about one asset, including the row version an update or archive has to send back.</summary>
/// <param name="RowVersion">Base64 of the SQL Server rowversion read with the record.</param>
public sealed record AssetDetails(
    int Id,
    string AssetCode,
    string? ComputerName,
    AssetType AssetType,
    AssetStatus Status,
    string? SerialNumber,
    string? Description,
    NamedReference Brand,
    NamedReference Model,
    NamedReference City,
    NamedReference Department,
    NamedReference? Location,
    ActiveAssignmentInfo? ActiveAssignment,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string RowVersion);

/// <summary>Who holds the asset now.</summary>
public sealed record ActiveAssignmentInfo(
    int Id,
    int EmployeeId,
    string UserName,
    string DisplayName,
    string? AssignmentDescription,
    DateTimeOffset AssignedAt,
    string AssignedBy);
