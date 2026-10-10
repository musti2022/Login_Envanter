using System.Globalization;
using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Application.Exports;
using EnterpriseInventory.Application.Reports;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EnterpriseInventory.Api.Reports;

/// <summary>
/// <c>/api/reports</c>: the report screen, read only. Signed-in Administrators only (the fallback policy). Each report
/// has an <c>/export</c> twin that writes the same rows to an .xlsx file.
/// </summary>
internal static class ReportEndpoints
{
    public const string Path = "/api/reports";

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var reports = endpoints.MapGroup(Path);

        // "Envanter özeti": assets by status per city, department, location, brand, model, type or status.
        reports.MapGet("/asset-summary", AssetSummaryAsync);
        reports.MapGet("/asset-summary/export", ExportAssetSummaryAsync);

        // "Zimmet hareketleri": assignments given and returned in a period.
        reports.MapGet("/assignments", AssignmentsAsync);
        reports.MapGet("/assignments/export", ExportAssignmentsAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<AssetSummary>, ValidationProblem>> AssetSummaryAsync(
        [AsParameters] AssetSummaryRequest request, AssetSummaryService summaries, CancellationToken cancellationToken)
    {
        var result = await summaries.GetAsync(request, cancellationToken);
        return result.Summary is { } summary ? TypedResults.Ok(summary) : ApiResults.ValidationProblem(result.Errors!);
    }

    private static async Task<IResult> ExportAssetSummaryAsync(
        [AsParameters] AssetSummaryRequest request, AssetSummaryService summaries, ISpreadsheetWriter writer, CancellationToken cancellationToken)
    {
        var result = await summaries.ExportAsync(request, cancellationToken);
        return result.Spreadsheet is { } spreadsheet
            ? File(spreadsheet, result.FileName!, writer)
            : ApiResults.ValidationProblem(result.Errors!);
    }

    private static async Task<Results<Ok<AssignmentReport>, ValidationProblem>> AssignmentsAsync(
        [AsParameters] AssignmentReportRequest request, AssignmentReportService movements, CancellationToken cancellationToken)
    {
        var result = await movements.ListAsync(request, cancellationToken);
        return result.Report is { } report ? TypedResults.Ok(report) : ApiResults.ValidationProblem(result.Errors!);
    }

    private static async Task<IResult> ExportAssignmentsAsync(
        [AsParameters] AssignmentReportRequest request, AssignmentReportService movements, ISpreadsheetWriter writer, CancellationToken cancellationToken)
    {
        var result = await movements.ExportAsync(request, cancellationToken);
        return result.Outcome switch
        {
            AssignmentExportOutcome.Invalid => ApiResults.ValidationProblem(result.Errors!),
            AssignmentExportOutcome.TooManyRows => ApiResults.Problem(
                StatusCodes.Status400BadRequest,
                "Aktarılacak hareket sayısı sınırı aşıyor.",
                string.Create(
                    CultureInfo.GetCultureInfo("tr-TR"),
                    $"Filtrelerle eşleşen {result.MatchCount:N0} hareket var; bir dosyaya en fazla {result.MaxRows:N0} hareket aktarılabilir. Tarih aralığını veya filtreleri daraltıp tekrar deneyin."),
                "export_too_large"),
            _ => File(result.Spreadsheet!, result.FileName!, writer),
        };
    }

    private static FileContentHttpResult File(Spreadsheet spreadsheet, string fileName, ISpreadsheetWriter writer)
    {
        using var file = new MemoryStream();
        writer.Write(spreadsheet, file);
        return TypedResults.File(file.ToArray(), writer.ContentType, fileName + writer.FileExtension);
    }
}
