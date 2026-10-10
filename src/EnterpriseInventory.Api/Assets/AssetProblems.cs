using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Domain.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EnterpriseInventory.Api.Assets;

/// <summary>Turkish answers for business rules that refused a change to an asset or its assignment (always <c>409</c>).</summary>
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
        [DomainErrors.Asset.AlreadyAssigned] = (
            "asset_already_assigned",
            "Demirbaş zaten bir çalışana zimmetli.",
            "Başka bir zimmet kaydedilmiş olabilir. Sayfayı yenileyin; yeniden zimmetlemek için önce iadesini alın."),
        [DomainErrors.Asset.NotAvailableForAssignment] = (
            "asset_not_available",
            "Yalnızca boşta olan demirbaş zimmetlenebilir.",
            "Arızalı veya hurda demirbaşın durumunu önce Boşta olarak değiştirin."),
        [DomainErrors.Asset.NotAssigned] = (
            "asset_not_assigned",
            "Demirbaş kimseye zimmetli değil.",
            "İade edilecek bir zimmet yok; iade daha önce alınmış olabilir. Sayfayı yenileyin."),
        [DomainErrors.Asset.AssignmentOverlapsHistory] = (
            "assignment_overlaps_history",
            "Zimmet tarihi önceki iadeden önce olamaz.",
            "Sunucu saatini kontrol edin ve tekrar deneyin."),
        [DomainErrors.Asset.ReturnBeforeAssignment] = (
            "return_before_assignment",
            "İade tarihi zimmet tarihinden önce olamaz.",
            "Sunucu saatini kontrol edin ve tekrar deneyin."),
        [DomainErrors.Employee.Inactive] = (
            "employee_inactive",
            "Çalışanın Active Directory hesabı pasif.",
            "Pasif hesaplı çalışana demirbaş zimmetlenemez."),
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
