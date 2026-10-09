using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Domain.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EnterpriseInventory.Api.Assets;

/// <summary>Turkish answers for business rules that refused a change to an asset (always <c>409</c>).</summary>
internal static class AssetProblems
{
    private static readonly Dictionary<string, (string Code, string Title, string Detail)> Rules = new()
    {
        [DomainErrors.Asset.Archived] = (
            "asset_archived",
            "Arşivlenmiş demirbaş değiştirilemez.",
            "Bu demirbaş arşivlendiği için yalnızca görüntülenebilir."),
        [DomainErrors.Asset.AlreadyArchived] = (
            "asset_already_archived",
            "Demirbaş zaten arşivlenmiş.",
            "Bu demirbaş daha önce arşivlenmiş."),
        [DomainErrors.Asset.HasActiveAssignment] = (
            "asset_assigned",
            "Zimmetli demirbaşta bu işlem yapılamaz.",
            "Önce demirbaşın iadesini alın, sonra tekrar deneyin."),
        [DomainErrors.Asset.StatusRequiresAssignmentFlow] = (
            "status_requires_assignment",
            "Zimmetli durumu yalnızca demirbaş zimmetlenerek verilir.",
            "Demirbaşı bir çalışana zimmetlemek için zimmet işlemini kullanın."),
        [DomainErrors.Asset.InactiveReference] = (
            "inactive_reference",
            "Seçilen tanımlardan biri pasif.",
            "Pasif marka, model, şehir, departman veya lokasyon yeni seçimlerde kullanılamaz."),
        [DomainErrors.Asset.LocationCityMismatch] = (
            "location_city_mismatch",
            "Seçilen lokasyon seçilen şehirde değil.",
            "Lokasyonu demirbaşın şehrinden seçin."),
    };

    public static ProblemHttpResult ForRule(string? ruleCode) =>
        ruleCode is not null && Rules.TryGetValue(ruleCode, out var rule)
            ? ApiResults.Problem(StatusCodes.Status409Conflict, rule.Title, rule.Detail, rule.Code)
            : ApiResults.Problem(
                StatusCodes.Status409Conflict,
                "İşlem, kaydın güncel durumuyla çakışıyor.",
                "Sayfayı yenileyip tekrar deneyin.",
                "rule_violated");
}
