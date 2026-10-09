using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.Infrastructure.Assets;

internal sealed class AssetStore(ApplicationDbContext db) : IAssetStore
{
    public async Task<PagedResult<AssetListItem>> ListAsync(AssetListCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var assets = db.Assets.AsNoTracking().Where(a => !a.IsDeleted);
        var totalCount = await assets.CountAsync(cancellationToken).ConfigureAwait(false);

        // The ID breaks ties, so every row appears on exactly one page.
        var rows = await assets
            .OrderBy(a => a.AssetCode)
            .ThenBy(a => a.Id)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(a => new
            {
                a.Id,
                a.AssetCode,
                a.ComputerName,
                BrandName = a.Brand.Name,
                ModelName = a.Model.Name,
                a.SerialNumber,
                a.AssetType,
                a.Status,
                CityName = a.City.Name,
                DepartmentName = a.Department.Name,
                LocationName = a.Location != null ? a.Location.Name : null,
                Holder = a.Assignments
                    .Where(x => x.ReturnedAt == null)
                    .Select(x => new { x.Employee.SamAccountName, x.Employee.DisplayName, x.AssignmentDescription })
                    .FirstOrDefault(),
                a.IsDeleted,
                a.CreatedAt,
                a.UpdatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows.ConvertAll(r => new AssetListItem(
            r.Id,
            r.AssetCode,
            r.ComputerName,
            r.BrandName,
            r.ModelName,
            r.SerialNumber,
            r.AssetType,
            r.Status,
            r.CityName,
            r.DepartmentName,
            r.LocationName,
            r.Holder?.SamAccountName,
            r.Holder?.DisplayName,
            r.Holder?.AssignmentDescription,
            r.IsDeleted,
            r.CreatedAt,
            r.UpdatedAt));
        return new PagedResult<AssetListItem>(items, criteria.Page, criteria.PageSize, totalCount);
    }

    public async Task<AssetDetails?> FindAsync(int id, CancellationToken cancellationToken)
    {
        var row = await db.Assets.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id,
                a.AssetCode,
                a.ComputerName,
                a.AssetType,
                a.Status,
                a.SerialNumber,
                a.Description,
                Brand = new NamedReference(a.Brand.Id, a.Brand.Name),
                Model = new NamedReference(a.Model.Id, a.Model.Name),
                City = new NamedReference(a.City.Id, a.City.Name),
                Department = new NamedReference(a.Department.Id, a.Department.Name),
                Location = a.Location != null ? new NamedReference(a.Location.Id, a.Location.Name) : null,
                ActiveAssignment = a.Assignments
                    .Where(x => x.ReturnedAt == null)
                    .Select(x => new ActiveAssignmentInfo(
                        x.Id, x.EmployeeId, x.Employee.SamAccountName, x.Employee.DisplayName, x.AssignmentDescription, x.AssignedAt, x.AssignedBy))
                    .FirstOrDefault(),
                a.IsDeleted,
                a.CreatedAt,
                a.CreatedBy,
                a.UpdatedAt,
                a.UpdatedBy,
                a.RowVersion,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? null
            : new AssetDetails(
                row.Id,
                row.AssetCode,
                row.ComputerName,
                row.AssetType,
                row.Status,
                row.SerialNumber,
                row.Description,
                row.Brand,
                row.Model,
                row.City,
                row.Department,
                row.Location,
                row.ActiveAssignment,
                row.IsDeleted,
                row.CreatedAt,
                row.CreatedBy,
                row.UpdatedAt,
                row.UpdatedBy,
                Convert.ToBase64String(row.RowVersion));
    }
}
