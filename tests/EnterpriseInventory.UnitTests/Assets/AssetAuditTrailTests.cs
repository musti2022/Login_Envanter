using System.Text.Json;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Infrastructure.Assets;

namespace EnterpriseInventory.UnitTests.Assets;

public class AssetAuditTrailTests
{
    private readonly City _istanbul = City.Create("İstanbul");
    private readonly Department _it = Department.Create("Bilgi İşlem");

    [Fact]
    public void A_snapshot_records_every_field_with_lookup_names()
    {
        var asset = NewAsset();

        var json = JsonDocument.Parse(AssetAuditTrail.Serialize(AssetAuditTrail.Snapshot(asset))).RootElement;

        Assert.Equal("DMR-1", json.GetProperty("assetCode").GetString());
        Assert.Equal("Laptop", json.GetProperty("assetType").GetString());
        Assert.Equal("Available", json.GetProperty("status").GetString());
        Assert.Equal("Dell", json.GetProperty("brandName").GetString());
        Assert.Equal("İstanbul", json.GetProperty("cityName").GetString());
        Assert.False(json.GetProperty("isArchived").GetBoolean());
    }

    [Fact]
    public void Turkish_letters_stay_readable_but_html_is_escaped()
    {
        var asset = NewAsset(description: "Şarj <script>");

        var json = AssetAuditTrail.Serialize(AssetAuditTrail.Snapshot(asset));

        Assert.Contains("İstanbul", json, StringComparison.Ordinal);
        Assert.Contains("Şarj", json, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", json, StringComparison.Ordinal);
    }

    [Fact]
    public void An_edit_is_recorded_once_per_kind_of_change_with_only_the_changed_fields()
    {
        var asset = NewAsset();
        var before = AssetAuditTrail.Snapshot(asset);
        asset.UpdateDetails("DMR-1", AssetType.Laptop, "PC-NEW", "SN-1", null);
        asset.ChangeStatus(AssetStatus.Faulty);
        asset.ChangeLocation(_istanbul, Department.Create("Muhasebe"), null);

        var changes = AssetAuditTrail.Changes(before, AssetAuditTrail.Snapshot(asset)).ToList();

        Assert.Equal([AuditAction.Updated, AuditAction.LocationChanged, AuditAction.StatusChanged], changes.Select(c => c.Action));
        Assert.Equal("""{"computerName":null}""", changes[0].OldValues);
        Assert.Equal("""{"computerName":"PC-NEW"}""", changes[0].NewValues);
        Assert.Equal("""{"departmentName":"Muhasebe"}""", changes[1].NewValues); // unsaved lookups share ID 0
        Assert.Equal("""{"status":"Faulty"}""", changes[2].NewValues);
    }

    [Fact]
    public void Nothing_changed_nothing_recorded()
    {
        var asset = NewAsset();

        Assert.Empty(AssetAuditTrail.Changes(AssetAuditTrail.Snapshot(asset), AssetAuditTrail.Snapshot(asset)));
    }

    private Asset NewAsset(string? description = null) =>
        Asset.Create("DMR-1", AssetType.Laptop, AssetModel.Create(Brand.Create("Dell"), "Latitude"), _istanbul, _it, serialNumber: "SN-1", description: description);
}
