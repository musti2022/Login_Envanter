using EnterpriseInventory.Domain.Assets;

namespace EnterpriseInventory.Application.Assets;

/// <summary>
/// Turkish names of statuses, types and sort columns, for files the server writes (Excel). The same words as the web
/// app's <c>labels.ts</c>, so a list reads the same on screen and in a file.
/// </summary>
public static class AssetLabels
{
    public static string Of(AssetStatus status) => status switch
    {
        AssetStatus.Available => "Boşta",
        AssetStatus.Assigned => "Zimmetli",
        AssetStatus.Faulty => "Arızalı",
        AssetStatus.Retired => "Hurda",
        _ => status.ToString(),
    };

    public static string Of(AssetType type) => type switch
    {
        AssetType.Desktop => "Masaüstü",
        AssetType.Laptop => "Dizüstü",
        AssetType.Monitor => "Monitör",
        AssetType.Printer => "Yazıcı",
        AssetType.Phone => "Telefon",
        AssetType.Tablet => "Tablet",
        AssetType.Server => "Sunucu",
        AssetType.NetworkDevice => "Ağ cihazı",
        AssetType.Peripheral => "Çevre birimi",
        AssetType.Other => "Diğer",
        _ => type.ToString(),
    };

    public static string Of(AssetSortField field) => field switch
    {
        AssetSortField.AssetCode => "Demirbaş Kodu",
        AssetSortField.ComputerName => "Bilgisayar Adı",
        AssetSortField.SerialNumber => "Seri No",
        AssetSortField.BrandName => "Marka",
        AssetSortField.ModelName => "Model",
        AssetSortField.AssetType => "Tür",
        AssetSortField.Status => "Durum",
        AssetSortField.CityName => "Şehir",
        AssetSortField.DepartmentName => "Departman",
        AssetSortField.LocationName => "Lokasyon",
        AssetSortField.AssignedUserName => "Kullanıcı Adı",
        AssetSortField.AssignedDisplayName => "Ad Soyad",
        AssetSortField.CreatedAt => "Eklenme Zamanı",
        AssetSortField.UpdatedAt => "Son Değişiklik",
        _ => field.ToString(),
    };
}
