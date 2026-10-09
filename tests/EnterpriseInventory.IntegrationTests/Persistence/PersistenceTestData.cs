using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Domain.Organization;
using Microsoft.Data.SqlClient;

namespace EnterpriseInventory.IntegrationTests.Persistence;

/// <summary>Unsaved domain objects with unique names, so tests sharing a database never collide.</summary>
internal static class PersistenceTestData
{
    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>An available asset with its own brand, model, city, department and (optionally) location.</summary>
    public static Asset NewAsset(string? assetCode = null, string? serialNumber = null, bool withLocation = true)
    {
        var city = City.Create(Unique("Şehir"));
        return Asset.Create(
            assetCode ?? Unique("DMR"),
            AssetType.Laptop,
            AssetModel.Create(Brand.Create(Unique("Marka")), Unique("Model")),
            city,
            Department.Create(Unique("Birim")),
            withLocation ? Location.Create(city, Unique("Konum")) : null,
            computerName: "PC-TEST-01",
            serialNumber: serialNumber,
            description: "Entegrasyon testi");
    }

    public static Employee NewEmployee(DateTimeOffset syncedAt) =>
        Employee.Create(Guid.NewGuid(), $"u{Guid.NewGuid():N}"[..13], "Test Çalışan", null, "Muhasebe", null, isActive: true, syncedAt);

    /// <summary>Asserts that <paramref name="action"/> fails in SQL Server on the named constraint or index.</summary>
    public static async Task AssertViolatesAsync(string constraintName, Func<Task> action)
    {
        var exception = await Record.ExceptionAsync(action);
        var sqlException = exception as SqlException ?? exception?.InnerException as SqlException;

        Assert.True(sqlException is not null, $"Expected SQL Server to reject the change with {constraintName}, got: {exception?.GetType().Name ?? "no error"}.");
        Assert.Contains(constraintName, sqlException.Message, StringComparison.Ordinal);
    }
}
