using System.Diagnostics;
using System.Globalization;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.Infrastructure.Persistence.Interceptors;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Performance;

/// <summary>What <see cref="LoadSeed"/> wrote, for the measurements to pick their targets from.</summary>
internal sealed record LoadData(
    int AssetCount,
    int AssignmentCount,
    int AuditLogCount,
    int EmployeeCount,
    TimeSpan SeedTime,
    int BusiestCityId,
    int SampleAssetId,
    string SampleAssetCode,
    string SampleEmployeeName);

/// <summary>
/// A representative inventory of a company with offices in every province: 81 cities with about 400 locations, 40
/// departments, 25 brands with 200 models, 3,000 employees, <c>assets</c> assets (20,000 by default) of which about
/// 70% are assigned, and two years of assignments and returns, every one with its audit records. Cities are skewed
/// the way real ones are: the first few hold most assets.
/// </summary>
internal static class LoadSeed
{
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);

    private const int Cities = 81;
    private const int Departments = 40;
    private const int Brands = 25;
    private const int ModelsPerBrand = 8;
    private const int Employees = 3000;
    private const int Batch = 2000;

    private static readonly AssetType[] Types =
        [AssetType.Laptop, AssetType.Laptop, AssetType.Laptop, AssetType.Desktop, AssetType.Desktop, AssetType.Monitor, AssetType.Monitor, AssetType.Printer];

    public static async Task<LoadData> SeedAsync(string connectionString, int assets, int employees = Employees)
    {
        var watch = Stopwatch.StartNew();
        var random = new Random(35);
        var clock = new TestClock(Now.AddYears(-2));

        ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(new AuditableEntityInterceptor(new TestCurrentUser("yukleme.seed"), clock))
            .Options);

        var brands = Enumerable.Range(1, Brands).Select(i => Brand.Create($"Marka {i:D2}")).ToList();
        var models = brands.SelectMany((b, i) => Enumerable.Range(1, ModelsPerBrand).Select(m => AssetModel.Create(b, $"Model {i + 1:D2}-{m}"))).ToList();
        var cities = Enumerable.Range(1, Cities).Select(i => City.Create($"İl {i:D2}")).ToList();
        var locations = cities.Select((c, i) => Enumerable.Range(1, i < 3 ? 25 : 4).Select(l => Location.Create(c, $"Lokasyon {i + 1:D2}-{l:D2}")).ToList()).ToList();
        var departments = Enumerable.Range(1, Departments).Select(i => Department.Create($"Departman {i:D2}")).ToList();
        var people = Enumerable.Range(1, employees)
            .Select(i => Employee.Create(Guid.NewGuid(), $"calisan.{i:D4}", $"Çalışan {i:D4} Yılmaz", null, $"Departman {i % Departments + 1:D2}", null, isActive: true, Now))
            .ToList();

        await using (var db = Context())
        {
            db.AddRange(models);
            db.AddRange(locations.SelectMany(l => l));
            db.AddRange(departments);
            db.AddRange(people);
            await db.SaveChangesAsync();
        }

        var assignmentCount = 0;
        for (var start = 0; start < assets; start += Batch)
        {
            var batch = new List<Asset>(Batch);
            await using var db = Context();
            db.AttachRange(models);
            db.AttachRange(locations.SelectMany(l => l));
            db.AttachRange(departments);
            db.AttachRange(people);

            for (var i = start; i < Math.Min(start + Batch, assets); i++)
            {
                // Squaring a uniform number puts most assets in the first cities.
                var city = (int)(Math.Pow(random.NextDouble(), 2.2) * Cities);
                var cityLocations = locations[city];
                var location = random.Next(5) == 0 ? null : cityLocations[random.Next(cityLocations.Count)];
                var asset = Asset.Create(
                    $"DMR-{i + 1:D6}",
                    Types[i % Types.Length],
                    models[random.Next(models.Count)],
                    cities[city],
                    departments[random.Next(departments.Count)],
                    location,
                    $"PC-{city + 1:D2}-{i + 1:D6}",
                    $"SN{i + 1:D8}");

                // Up to three earlier periods spread over the two years, then 70% are with someone now.
                var at = Now.AddDays(-random.Next(30, 730));
                for (var p = random.Next(4); p > 0; p--)
                {
                    var returnedAt = at.AddDays(20 + random.Next(150));
                    if (returnedAt >= Now)
                    {
                        break;
                    }

                    asset.Assign(people[random.Next(people.Count)], "Dizüstü bilgisayar", null, "admin.bir", at);
                    asset.Return("admin.iki", returnedAt);
                    at = returnedAt.AddDays(random.Next(10));
                    assignmentCount++;
                }

                var roll = random.Next(100);
                if (roll < 70)
                {
                    asset.Assign(people[random.Next(people.Count)], "Dizüstü bilgisayar + çanta", null, "admin.bir", Min(at.AddDays(random.Next(30)), Now));
                    assignmentCount++;
                }
                else if (roll < 77)
                {
                    asset.ChangeStatus(AssetStatus.Faulty);
                }
                else if (roll < 80)
                {
                    asset.ChangeStatus(AssetStatus.Retired);
                }
                else if (roll < 82)
                {
                    asset.Archive();
                }

                db.Add(asset);
                batch.Add(asset);
            }

            await db.SaveChangesAsync();

            // The audit trail the API would have written for the same history, now that the assets have their IDs.
            db.AuditLogs.AddRange(batch.SelectMany(AuditTrail));
            await db.SaveChangesAsync();
        }

        return await DescribeAsync(connectionString, watch.Elapsed);
    }

    /// <summary>The counts and sample records of an inventory seeded earlier.</summary>
    public static async Task<LoadData> DescribeAsync(string connectionString, TimeSpan seedTime)
    {
        await using var read = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options);
        var assets = await read.Assets.IgnoreQueryFilters().CountAsync();
        var employees = await read.Employees.CountAsync();
        var busiest = await read.Assets.GroupBy(a => a.CityId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        var sample = await read.Assets.Where(a => a.Status == AssetStatus.Assigned).OrderBy(a => a.Id).Skip(assets / 3).Select(a => new { a.Id, a.AssetCode }).FirstAsync();
        var employee = await read.Employees.OrderBy(e => e.Id).Skip(Math.Min(1234, employees - 1)).Select(e => e.DisplayName).FirstAsync();
        return new LoadData(
            assets,
            await read.AssetAssignments.IgnoreQueryFilters().CountAsync(),
            await read.AuditLogs.CountAsync(),
            employees,
            seedTime,
            busiest,
            sample.Id,
            sample.AssetCode,
            employee);
    }

    private static IEnumerable<AuditLog> AuditTrail(Asset asset)
    {
        var id = asset.Id.ToString(CultureInfo.InvariantCulture);
        var location = asset.LocationId?.ToString(CultureInfo.InvariantCulture) ?? "null";
        var created = asset.Assignments.Count > 0 ? asset.Assignments.Min(a => a.AssignedAt).AddDays(-3) : Now.AddDays(-730);
        yield return Log(
            AuditAction.Created,
            null,
            $$"""{"AssetCode":"{{asset.AssetCode}}","AssetType":"{{asset.AssetType}}","Status":"Available","ModelId":{{asset.ModelId}},"CityId":{{asset.CityId}},"DepartmentId":{{asset.DepartmentId}},"LocationId":{{location}},"ComputerName":"{{asset.ComputerName}}","SerialNumber":"{{asset.SerialNumber}}"}""",
            created,
            "admin.bir");
        foreach (var period in asset.Assignments.OrderBy(a => a.AssignedAt))
        {
            var holder = period.Employee.SamAccountName;
            yield return Log(
                AuditAction.Assigned,
                """{"Status":"Available"}""",
                $$"""{"Status":"Assigned","Employee":"{{holder}}","AssignmentDescription":"{{period.AssignmentDescription}}"}""",
                period.AssignedAt,
                period.AssignedBy);
            if (period.ReturnedAt is { } returnedAt)
            {
                yield return Log(AuditAction.Returned, $$"""{"Status":"Assigned","Employee":"{{holder}}"}""", """{"Status":"Available"}""", returnedAt, period.ReturnedBy ?? "admin.iki");
            }
        }

        if (asset.Status is AssetStatus.Faulty or AssetStatus.Retired)
        {
            yield return Log(AuditAction.StatusChanged, """{"Status":"Available"}""", $$"""{"Status":"{{asset.Status}}"}""", Now.AddDays(-20), "admin.iki");
        }

        if (asset.IsDeleted)
        {
            yield return Log(AuditAction.Archived, """{"IsDeleted":false}""", """{"IsDeleted":true}""", Now.AddDays(-10), "admin.bir");
        }

        AuditLog Log(AuditAction action, string? oldValues, string newValues, DateTimeOffset at, string by) =>
            AuditLog.Create("Asset", id, action, oldValues, newValues, by, at, Guid.NewGuid().ToString("N"));
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    public static string Number(long value) => value.ToString("N0", CultureInfo.GetCultureInfo("tr-TR"));
}
