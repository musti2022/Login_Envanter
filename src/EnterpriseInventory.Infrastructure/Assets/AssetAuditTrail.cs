using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;

namespace EnterpriseInventory.Infrastructure.Assets;

/// <summary>
/// The old and new values an asset's audit records carry: a flat JSON object of the fields that changed, with
/// lookup IDs next to the names they had at the time, so the record stays readable after a rename.
/// </summary>
internal static class AssetAuditTrail
{
    /// <summary>Fields that move the asset; a change to any of them is recorded as <see cref="AuditAction.LocationChanged"/>.</summary>
    private static readonly HashSet<string> LocationFields = ["cityId", "cityName", "departmentId", "departmentName", "locationId", "locationName"];

    // Turkish letters stay readable in the database; characters that matter in HTML are still escaped.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() },
    };

    public static string EntityName => nameof(Asset);

    public static string EntityId(Asset asset) => asset.Id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Every recorded field of the asset, in a fixed order.</summary>
    public static Dictionary<string, object?> Snapshot(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new Dictionary<string, object?>
        {
            ["assetCode"] = asset.AssetCode,
            ["assetType"] = asset.AssetType,
            ["status"] = asset.Status,
            ["computerName"] = asset.ComputerName,
            ["serialNumber"] = asset.SerialNumber,
            ["description"] = asset.Description,
            ["brandId"] = asset.BrandId,
            ["brandName"] = asset.Brand.Name,
            ["modelId"] = asset.ModelId,
            ["modelName"] = asset.Model.Name,
            ["cityId"] = asset.CityId,
            ["cityName"] = asset.City.Name,
            ["departmentId"] = asset.DepartmentId,
            ["departmentName"] = asset.Department.Name,
            ["locationId"] = asset.LocationId,
            ["locationName"] = asset.Location?.Name,
            ["isArchived"] = asset.IsDeleted,
        };
    }

    /// <summary>
    /// The audit records for an edit, one per kind of change: <see cref="AuditAction.StatusChanged"/>,
    /// <see cref="AuditAction.LocationChanged"/> and <see cref="AuditAction.Updated"/> for everything else.
    /// Nothing changed, nothing recorded.
    /// </summary>
    public static IEnumerable<(AuditAction Action, string OldValues, string NewValues)> Changes(
        Dictionary<string, object?> before, Dictionary<string, object?> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var changed = after.Keys.Where(key => !Equals(before[key], after[key])).ToList();
        var groups = changed.GroupBy(key => key switch
        {
            "status" => AuditAction.StatusChanged,
            "isArchived" => AuditAction.Archived,
            _ when LocationFields.Contains(key) => AuditAction.LocationChanged,
            _ => AuditAction.Updated,
        });

        foreach (var group in groups.OrderBy(g => g.Key))
        {
            yield return (group.Key, Serialize(group.ToDictionary(key => key, key => before[key])), Serialize(group.ToDictionary(key => key, key => after[key])));
        }
    }

    public static string Serialize(Dictionary<string, object?> values) => JsonSerializer.Serialize(values, Json);

    /// <summary>Stored values back as JSON, for the history; <c>null</c> stays <c>null</c>.</summary>
    public static JsonElement? Parse(string? values)
    {
        if (values is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(values);
        return document.RootElement.Clone();
    }
}
