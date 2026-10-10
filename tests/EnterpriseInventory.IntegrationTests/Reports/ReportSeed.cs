using System.Globalization;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.IntegrationTests.Persistence;

namespace EnterpriseInventory.IntegrationTests.Reports;

/// <summary>
/// The report tests' data: 16 assets on four models of three brands, three cities (two with locations, one without),
/// three departments, every status, two archived; assignments and returns on both sides of Istanbul day boundaries.
/// </summary>
internal static class ReportSeed
{
    /// <summary>10 November 2026, 09:00 in Istanbul.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 10, 6, 0, 0, TimeSpan.Zero);

    public static async Task SeedAsync(SqlServerDatabaseFixture fixture, string connectionString)
    {
        var dell = Brand.Create("Dell");
        var hp = Brand.Create("HP");
        var lenovo = Brand.Create("Lenovo");
        AssetModel[] models =
        [
            AssetModel.Create(dell, "Latitude 5440"),
            AssetModel.Create(dell, "OptiPlex 7010"),
            AssetModel.Create(hp, "EliteBook 840"),
            AssetModel.Create(lenovo, "ThinkPad T14"),
        ];
        var istanbul = City.Create("İstanbul");
        var ankara = City.Create("Ankara");
        var izmir = City.Create("İzmir");
        Location?[][] locationsOf =
        [
            [Location.Create(istanbul, "Merkez Ofis"), Location.Create(istanbul, "Ümraniye Depo"), null],
            [Location.Create(ankara, "Çankaya Ofis"), null],
            [null],
        ];
        City[] cities = [istanbul, ankara, izmir];
        Department[] departments = [Department.Create("Bilgi İşlem"), Department.Create("Muhasebe"), Department.Create("İnsan Kaynakları")];
        AssetType[] types = [AssetType.Laptop, AssetType.Desktop, AssetType.Monitor, AssetType.Laptop, AssetType.Printer];

        var assets = new List<Asset>();
        for (var i = 0; i < 16; i++)
        {
            var city = i % 3;
            var location = locationsOf[city][i / 3 % locationsOf[city].Length];
            assets.Add(Asset.Create(
                $"RP-{i:D2}", types[i % types.Length], models[i % models.Length], cities[city], departments[i / 2 % 3], location));
        }

        var ayse = Employee.Create(Guid.NewGuid(), "ayse.yilmaz", "Ayşe Yılmaz", null, "Muhasebe", null, isActive: true, Now);
        var mehmet = Employee.Create(Guid.NewGuid(), "mehmet.kaya", "Mehmet Kaya", null, "Bilgi İşlem", null, isActive: true, Now);

        // 23:30 on 30 September in Istanbul (September) and 00:30 on 1 October (October): both 30 September in UTC.
        assets[0].Assign(ayse, "Dizüstü + çanta", null, "admin.bir", At("2026-09-30T20:30:00Z"));
        assets[1].Assign(mehmet, null, null, "admin.iki", At("2026-09-30T21:30:00Z"));

        // 23:59 on 31 October in Istanbul: still October.
        assets[2].Assign(ayse, null, null, "admin.bir", At("2026-10-31T20:59:00Z"));

        // Given in October, returned at 00:00 on 1 November in Istanbul (November), then found faulty.
        assets[3].Assign(mehmet, null, null, "admin.bir", At("2026-10-05T08:00:00Z"));
        assets[3].Return("admin.iki", At("2026-10-31T21:00:00Z"));
        assets[3].ChangeStatus(AssetStatus.Faulty);

        // Given in August, returned in October.
        assets[4].Assign(ayse, null, null, "admin.iki", At("2026-08-10T08:00:00Z"));
        assets[4].Return("admin.bir", At("2026-10-15T09:00:00Z"));
        assets[4].ChangeStatus(AssetStatus.Faulty);

        assets[5].ChangeStatus(AssetStatus.Retired);

        // Given and returned in October, then archived: its movements still count, the asset no longer does.
        assets[6].Assign(mehmet, null, null, "admin.bir", At("2026-10-02T07:00:00Z"));
        assets[6].Return("admin.bir", At("2026-10-03T07:00:00Z"));
        assets[6].Archive();
        assets[7].Archive();

        await using var context = fixture.CreateContextFor(connectionString);
        context.AddRange(assets);
        await context.SaveChangesAsync();
    }

    public static DateTimeOffset At(string moment) => DateTimeOffset.Parse(moment, CultureInfo.InvariantCulture);
}
