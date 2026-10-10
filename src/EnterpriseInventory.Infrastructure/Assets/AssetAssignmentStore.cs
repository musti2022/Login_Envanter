using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Employees;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnterpriseInventory.Infrastructure.Assets;

/// <summary>
/// Assignments and returns. Each is one transaction (run by the execution strategy): the asset is read with its
/// assignments, checked against the caller's row version and the domain rules, changed, saved, audited and
/// committed, or nothing is. Two guards stop a second active assignment under concurrent requests: the asset's
/// row version (the assignment also updates the asset row) and the filtered unique index
/// <see cref="ActiveAssignmentIndex"/> in SQL Server. Assignments of different assets to the same person at the
/// same moment take turns on the person's record (see <see cref="RefreshEmployeeAsync"/>), so neither is refused
/// because of the other. Locks are always taken in the same order (person, then asset), so they cannot deadlock.
/// </summary>
internal sealed partial class AssetAssignmentStore(
    ApplicationDbContext db,
    IAssetStore assets,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    TimeProvider timeProvider,
    ILogger<AssetAssignmentStore> logger) : IAssetAssignmentStore
{
    public const string ActiveAssignmentIndex = "UX_AssetAssignments_AssetId_Active";
    private const string EmployeeIndex = "IX_Employees_ObjectGuid";

    public async Task<AssetWriteResult> AssignAsync(int assetId, AssignmentDraft draft, byte[] rowVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(rowVersion);
        var userName = UserName();

        // A second attempt only when the same employee's record was added at the same moment by something that does
        // not take turns on it (see RefreshEmployeeAsync); the record then exists and is updated instead.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await db.InTransactionAsync(
                    async (transaction, token) =>
                    {
                        // The person's record first: it stays locked until the end, and the asset read below then
                        // finds it already loaded, as it is now.
                        var now = timeProvider.GetUtcNow();
                        var employee = await RefreshEmployeeAsync(draft.Employee, now, token).ConfigureAwait(false);
                        var asset = await LoadAsync(assetId, token).ConfigureAwait(false);
                        if (asset is null)
                        {
                            return AssetWriteResult.NotFound;
                        }

                        if (!db.MatchesClientVersion(asset, rowVersion))
                        {
                            LogConflict(assetId);
                            return AssetWriteResult.Conflict;
                        }

                        AssetAssignment assignment;
                        try
                        {
                            assignment = asset.Assign(employee, draft.AssignmentDescription, draft.Notes, userName, now);
                        }
                        catch (DomainException ex)
                        {
                            return AssetWriteResult.Rule(ex.Code);
                        }

                        // Saved first so the audit record can name the assignment by its ID.
                        await db.SaveChangesAsync(token).ConfigureAwait(false);
                        Audit(
                            asset,
                            AuditAction.Assigned,
                            new() { ["status"] = AssetStatus.Available },
                            new()
                            {
                                ["status"] = asset.Status,
                                ["assignmentId"] = assignment.Id,
                                ["employeeId"] = employee.Id,
                                ["employeeObjectGuid"] = employee.ObjectGuid,
                                ["employeeUserName"] = employee.SamAccountName,
                                ["employeeDisplayName"] = employee.DisplayName,
                                ["assignmentDescription"] = assignment.AssignmentDescription,
                                ["notes"] = assignment.Notes,
                                ["assignedAt"] = assignment.AssignedAt,
                            },
                            now);
                        await db.SaveChangesAsync(token).ConfigureAwait(false);
                        await transaction.CommitAsync(token).ConfigureAwait(false);
                        LogAssigned(assetId, asset.AssetCode, employee.SamAccountName);
                        return AssetWriteResult.Succeeded(null);
                    },
                    cancellationToken).ConfigureAwait(false);

                return await WithDetailsAsync(assetId, result, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                LogConflict(assetId);
                return AssetWriteResult.Conflict;
            }
            catch (DbUpdateException ex) when (ex.IsUniqueKeyViolation(ActiveAssignmentIndex))
            {
                // Another assignment of this asset was committed first; the database kept only that one.
                db.ChangeTracker.Clear();
                LogLostAssignmentRace(assetId);
                return AssetWriteResult.Rule(DomainErrors.Asset.AlreadyAssigned);
            }
            catch (DbUpdateException ex) when (attempt == 1 && ex.IsUniqueKeyViolation(EmployeeIndex))
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<AssetWriteResult> ReturnAsync(int assetId, byte[] rowVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);
        var userName = UserName();

        try
        {
            var result = await db.InTransactionAsync(
                async (transaction, token) =>
                {
                    var asset = await LoadAsync(assetId, token).ConfigureAwait(false);
                    if (asset is null)
                    {
                        return AssetWriteResult.NotFound;
                    }

                    if (!db.MatchesClientVersion(asset, rowVersion))
                    {
                        LogConflict(assetId);
                        return AssetWriteResult.Conflict;
                    }

                    var now = timeProvider.GetUtcNow();
                    AssetAssignment assignment;
                    try
                    {
                        assignment = asset.Return(userName, now);
                    }
                    catch (DomainException ex)
                    {
                        return AssetWriteResult.Rule(ex.Code);
                    }

                    Audit(
                        asset,
                        AuditAction.Returned,
                        new()
                        {
                            ["status"] = AssetStatus.Assigned,
                            ["assignmentId"] = assignment.Id,
                            ["employeeId"] = assignment.EmployeeId,
                            ["employeeUserName"] = assignment.Employee.SamAccountName,
                            ["employeeDisplayName"] = assignment.Employee.DisplayName,
                            ["assignedAt"] = assignment.AssignedAt,
                        },
                        new()
                        {
                            ["status"] = asset.Status,
                            ["returnedAt"] = assignment.ReturnedAt,
                        },
                        now);

                    // One SaveChanges: the closed assignment, the asset and the audit record together.
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    LogReturned(assetId, asset.AssetCode, assignment.Employee.SamAccountName);
                    return AssetWriteResult.Succeeded(null);
                },
                cancellationToken).ConfigureAwait(false);

            return await WithDetailsAsync(assetId, result, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            LogConflict(assetId);
            return AssetWriteResult.Conflict;
        }
    }

    public async Task<PagedResult<AssetAssignmentItem>?> ListAsync(int assetId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await db.Assets.IncludingArchived().AnyAsync(a => a.Id == assetId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        // The history of archived assets too: archiving keeps it.
        var assignments = db.AssetAssignments.IncludingArchived().AsNoTracking().Where(x => x.AssetId == assetId);
        var totalCount = await assignments.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await assignments
            .OrderByDescending(x => x.AssignedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AssetAssignmentItem(
                x.Id,
                x.EmployeeId,
                x.Employee.SamAccountName,
                x.Employee.DisplayName,
                x.Employee.Department,
                x.AssignmentDescription,
                x.Notes,
                x.AssignedAt,
                x.AssignedBy,
                x.ReturnedAt,
                x.ReturnedBy))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<AssetAssignmentItem>(items, page, pageSize, totalCount);
    }

    /// <summary>
    /// The asset with all its assignments (the domain checks the new period against the last return) and their
    /// employees, tracked. Archived assets too, so a change to one is refused as such instead of as "not found".
    /// The asset's row is locked first, until the transaction ends (UPDLOCK): every change of an asset or of its
    /// assignments updates that row, so the assignments read next go with the asset read, and another change of the
    /// same asset waits here, then sees this one's result (a clean 409) instead of failing later. Without the lock,
    /// a return committed between the two reads left an asset that says "assigned" with no open assignment (a 500).
    /// </summary>
    private async Task<Asset?> LoadAsync(int assetId, CancellationToken cancellationToken)
    {
        await db.Database
            .ExecuteSqlAsync($"SELECT [Id] FROM [Assets] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {assetId}", cancellationToken)
            .ConfigureAwait(false);
        return await db.Assets
            .IncludingArchived()
            .Include(a => a.Assignments)
            .ThenInclude(x => x.Employee)
            .AsSplitQuery()
            .SingleOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The employee's record, added or refreshed from what the directory says now. Assignments to the same person
    /// take turns here: an application lock named after the person (sp_getapplock, held until the transaction ends)
    /// makes another assignment to them wait until this one commits or rolls back, then read the record as it is.
    /// Without it the two would add the same record (duplicate key, a 500) or one would refresh it under the other
    /// (the record's row version, a 409 that names the asset although nobody changed it). Assignments to different
    /// people do not wait for each other.
    /// </summary>
    private async Task<Employee> RefreshEmployeeAsync(DirectoryPerson person, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var resource = $"Employee:{person.ObjectGuid:D}";
        await db.Database.ExecuteSqlAsync(
            $"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
            IF @result < 0 THROW 50001, 'The employee record could not be locked.', 1;
            """,
            cancellationToken).ConfigureAwait(false);
        var employee = await db.Employees.SingleOrDefaultAsync(e => e.ObjectGuid == person.ObjectGuid, cancellationToken).ConfigureAwait(false);
        var (sam, name, email, department, title) = (
            Fit(person.SamAccountName, Employee.SamAccountNameMaxLength)!,
            Fit(person.DisplayName, Employee.DisplayNameMaxLength)!,
            Fit(person.Email, Employee.EmailMaxLength),
            Fit(person.Department, Employee.DepartmentMaxLength),
            Fit(person.Title, Employee.TitleMaxLength));
        if (employee is null)
        {
            employee = Employee.Create(person.ObjectGuid, sam, name, email, department, title, person.IsEnabled, now);
            db.Employees.Add(employee);
        }
        else
        {
            employee.UpdateFromDirectory(sam, name, email, department, title, person.IsEnabled, now);
        }

        return employee;
    }

    /// <summary>Directory values longer than the column are cut rather than refused; the directory is the master copy.</summary>
    private static string? Fit(string? value, int maxLength) => value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;

    private async Task<AssetWriteResult> WithDetailsAsync(int assetId, AssetWriteResult result, CancellationToken cancellationToken) =>
        result.Outcome == AssetWriteOutcome.Succeeded
            ? AssetWriteResult.Succeeded(await assets.FindAsync(assetId, cancellationToken).ConfigureAwait(false))
            : result;

    private string UserName() =>
        currentUser.UserName ?? throw new InvalidOperationException("Assets can only be assigned or returned by a signed-in user.");

    private void Audit(Asset asset, AuditAction action, Dictionary<string, object?> oldValues, Dictionary<string, object?> newValues, DateTimeOffset now) =>
        db.AuditLogs.Add(AuditLog.Create(
            AssetAuditTrail.EntityName,
            AssetAuditTrail.EntityId(asset),
            action,
            AssetAuditTrail.Serialize(oldValues),
            AssetAuditTrail.Serialize(newValues),
            UserName(),
            now,
            requestContext.CorrelationId));

    [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} ({AssetCode}) assigned to {EmployeeUserName}")]
    private partial void LogAssigned(int assetId, string assetCode, string employeeUserName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} ({AssetCode}) returned by {EmployeeUserName}")]
    private partial void LogReturned(int assetId, string assetCode, string employeeUserName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Asset {AssetId} not saved: it was changed by someone else since the caller read it")]
    private partial void LogConflict(int assetId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Asset {AssetId} not assigned: another assignment of it was committed first")]
    private partial void LogLostAssignmentRace(int assetId);
}
