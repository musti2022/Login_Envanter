using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Domain.Organization;

namespace EnterpriseInventory.Domain.Assets;

/// <summary>
/// A company asset (demirbaş). Aggregate root for its assignments: an asset has at most one active
/// assignment, its status follows the assignment flow, and archiving is a soft delete.
/// </summary>
/// <remarks>
/// <see cref="Status"/> is the source of truth for "is assigned", so status changes and archiving are safe even
/// when <see cref="Assignments"/> was not loaded. <see cref="Assign"/> and <see cref="Return"/> work on the
/// assignment history and need it loaded; <see cref="Return"/> fails fast when it is not. The database
/// enforces "one active assignment" with a filtered unique index, and <see cref="AuditableEntity.RowVersion"/>
/// rejects concurrent edits.
/// </remarks>
public sealed class Asset : AuditableEntity
{
    public const int AssetCodeMaxLength = 50;
    public const int ComputerNameMaxLength = 64;
    public const int SerialNumberMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    private readonly List<AssetAssignment> _assignments = [];

    private Asset()
    {
    }

    public string AssetCode { get; private set; } = string.Empty;

    public string? ComputerName { get; private set; }

    public int BrandId { get; private set; }

    public Brand Brand { get; private set; } = null!;

    public int ModelId { get; private set; }

    public AssetModel Model { get; private set; } = null!;

    /// <summary>
    /// Optional; stored as <see cref="NormalizeSerialNumber"/> returns it. Uniqueness among non-empty values is
    /// enforced by a filtered index.
    /// </summary>
    public string? SerialNumber { get; private set; }

    public AssetType AssetType { get; private set; }

    public AssetStatus Status { get; private set; }

    public string? Description { get; private set; }

    public int CityId { get; private set; }

    public City City { get; private set; } = null!;

    public int DepartmentId { get; private set; }

    public Department Department { get; private set; } = null!;

    public int? LocationId { get; private set; }

    public Location? Location { get; private set; }

    /// <summary>Archived ("arşivlendi"). Archived assets are read-only and hidden from normal lists.</summary>
    public bool IsDeleted { get; private set; }

    public IReadOnlyCollection<AssetAssignment> Assignments => _assignments.AsReadOnly();

    public AssetAssignment? ActiveAssignment => _assignments.SingleOrDefault(a => a.IsActive);

    public static Asset Create(
        string assetCode,
        AssetType assetType,
        AssetModel model,
        City city,
        Department department,
        Location? location = null,
        string? computerName = null,
        string? serialNumber = null,
        string? description = null)
    {
        var asset = new Asset { Status = AssetStatus.Available };
        asset.UpdateDetails(assetCode, assetType, computerName, serialNumber, description);
        asset.ChangeModel(model);
        asset.ChangeLocation(city, department, location);
        return asset;
    }

    public void UpdateDetails(string assetCode, AssetType assetType, string? computerName, string? serialNumber, string? description)
    {
        EnsureNotArchived();

        // Validate everything first so a rejected update leaves the asset unchanged.
        var code = Guard.Required(assetCode, AssetCodeMaxLength, nameof(assetCode));
        var type = Guard.Defined(assetType, nameof(assetType));
        var name = Guard.Optional(computerName, ComputerNameMaxLength, nameof(computerName));
        var serial = Guard.Optional(NormalizeSerialNumber(serialNumber), SerialNumberMaxLength, nameof(serialNumber));
        var text = Guard.Optional(description, DescriptionMaxLength, nameof(description));

        AssetCode = code;
        AssetType = type;
        ComputerName = name;
        SerialNumber = serial;
        Description = text;
    }

    /// <summary>
    /// The stored form of a serial number: every whitespace character removed and letters upper-cased with
    /// invariant rules, so "5cd 1234 xyz" and "5CD1234XYZ" are the same number. Invariant rules turn both i and ı
    /// into I; the unique index's Turkish collation would otherwise treat "abci" and "ABCI" as different numbers.
    /// Other characters, such as dashes, are kept. Empty input is <c>null</c>, which the unique index ignores.
    /// </summary>
    public static string? NormalizeSerialNumber(string? serialNumber)
    {
        if (serialNumber is null)
        {
            return null;
        }

        var compact = string.Concat(serialNumber.Where(c => !char.IsWhiteSpace(c)));
        return compact.Length == 0 ? null : compact.ToUpperInvariant();
    }

    /// <summary>Sets the model and, from it, the brand, so the two can never disagree.</summary>
    public void ChangeModel(AssetModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Brand is null)
        {
            throw new ArgumentException("The model's brand must be loaded.", nameof(model));
        }

        EnsureNotArchived();
        if (!IsSame(Model, ModelId, model))
        {
            EnsureActive(model, "model");
            EnsureActive(model.Brand, "brand");
        }

        Model = model;
        ModelId = model.Id;
        Brand = model.Brand;
        BrandId = model.BrandId;
    }

    /// <summary>Moves the asset. Returns <c>true</c> when the city, department or location actually changed.</summary>
    public bool ChangeLocation(City city, Department department, Location? location)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(department);
        EnsureNotArchived();

        if (location is not null && !location.IsIn(city))
        {
            throw new DomainException(DomainErrors.Asset.LocationCityMismatch, "The location does not belong to the selected city.");
        }

        var sameCity = IsSame(City, CityId, city);
        var sameDepartment = IsSame(Department, DepartmentId, department);
        var sameLocation = location is null ? LocationId is null && Location is null : IsSame(Location, LocationId ?? 0, location);
        if (sameCity && sameDepartment && sameLocation)
        {
            return false;
        }

        // Only newly chosen values must be active; keeping an existing, since-deactivated value is allowed.
        if (!sameCity)
        {
            EnsureActive(city, "city");
        }

        if (!sameDepartment)
        {
            EnsureActive(department, "department");
        }

        if (location is not null && !sameLocation)
        {
            EnsureActive(location, "location");
        }

        City = city;
        CityId = city.Id;
        Department = department;
        DepartmentId = department.Id;
        Location = location;
        LocationId = location?.Id;
        return true;
    }

    /// <summary>
    /// Changes the status outside the assignment flow, e.g. marking an asset faulty or retired.
    /// <see cref="AssetStatus.Assigned"/> can only be reached through <see cref="Assign"/>, and an
    /// assigned asset has to be returned before its status can change.
    /// </summary>
    public void ChangeStatus(AssetStatus status)
    {
        Guard.Defined(status, nameof(status));
        EnsureNotArchived();

        if (status == AssetStatus.Assigned)
        {
            throw new DomainException(DomainErrors.Asset.StatusRequiresAssignmentFlow, "Use Assign to give the asset to an employee.");
        }

        if (IsAssigned)
        {
            throw new DomainException(DomainErrors.Asset.HasActiveAssignment, "Return the asset before changing its status.");
        }

        Status = status;
    }

    public AssetAssignment Assign(
        Employee employee,
        string? assignmentDescription,
        string? notes,
        string assignedBy,
        DateTimeOffset assignedAt)
    {
        ArgumentNullException.ThrowIfNull(employee);
        EnsureNotArchived();

        if (ActiveAssignment is not null)
        {
            throw new DomainException(DomainErrors.Asset.AlreadyAssigned, "The asset already has an active assignment.");
        }

        if (Status != AssetStatus.Available)
        {
            throw new DomainException(DomainErrors.Asset.NotAvailableForAssignment, $"Only available assets can be assigned; the status is {Status}.");
        }

        if (!employee.IsActive)
        {
            throw new DomainException(DomainErrors.Employee.Inactive, "The employee's directory account is disabled.");
        }

        // Periods must not overlap: the new holder cannot start before the previous holder returned the asset.
        var lastReturnedAt = _assignments.Max(a => a.ReturnedAt);
        if (assignedAt < lastReturnedAt)
        {
            throw new DomainException(DomainErrors.Asset.AssignmentOverlapsHistory, "The assignment time is earlier than the previous return.");
        }

        var assignment = new AssetAssignment(this, employee, assignmentDescription, notes, assignedBy, assignedAt);
        _assignments.Add(assignment);
        Status = AssetStatus.Assigned;
        return assignment;
    }

    /// <summary>Closes the active assignment. The assignment stays in <see cref="Assignments"/> as history.</summary>
    public AssetAssignment Return(string returnedBy, DateTimeOffset returnedAt)
    {
        EnsureNotArchived();

        var assignment = ActiveAssignment;
        if (assignment is null)
        {
            if (Status == AssetStatus.Assigned)
            {
                throw new InvalidOperationException("The asset is assigned but its assignments were not loaded.");
            }

            throw new DomainException(DomainErrors.Asset.NotAssigned, "The asset has no active assignment.");
        }

        assignment.Close(returnedBy, returnedAt);
        Status = AssetStatus.Available;
        return assignment;
    }

    /// <summary>Soft-deletes the asset. An assigned asset has to be returned first.</summary>
    public void Archive()
    {
        if (IsDeleted)
        {
            throw new DomainException(DomainErrors.Asset.AlreadyArchived, "The asset is already archived.");
        }

        if (IsAssigned)
        {
            throw new DomainException(DomainErrors.Asset.HasActiveAssignment, "Return the asset before archiving it.");
        }

        IsDeleted = true;
    }

    /// <summary>True when the asset is assigned, whether or not its assignments were loaded.</summary>
    private bool IsAssigned => Status == AssetStatus.Assigned || ActiveAssignment is not null;

    private void EnsureNotArchived()
    {
        if (IsDeleted)
        {
            throw new DomainException(DomainErrors.Asset.Archived, "Archived assets cannot be changed.");
        }
    }

    /// <summary>Same entity: same instance, or same database key once the candidate has been saved.</summary>
    private static bool IsSame(ReferenceDataEntity? current, int currentId, ReferenceDataEntity candidate) =>
        ReferenceEquals(current, candidate) || (candidate.Id != 0 && candidate.Id == currentId);

    private static void EnsureActive(ReferenceDataEntity reference, string kind)
    {
        if (!reference.IsActive)
        {
            throw new DomainException(DomainErrors.Asset.InactiveReference, $"The selected {kind} is inactive.");
        }
    }
}
