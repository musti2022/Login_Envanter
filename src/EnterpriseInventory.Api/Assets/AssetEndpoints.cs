using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Application.Assets;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseInventory.Api.Assets;

/// <summary>
/// <c>/api/assets</c>. Every endpoint needs a signed-in Administrator (the fallback policy); state-changing ones
/// also need the CSRF token (see CsrfProtectionMiddleware).
/// </summary>
internal static class AssetEndpoints
{
    public const string Path = "/api/assets";

    /// <summary>Far more than an asset's fields can fill, small enough that nobody can post large bodies.</summary>
    private const long MaxBodyBytes = 16 * 1024;

    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var assets = endpoints.MapGroup(Path);
        assets.MapGet(string.Empty, ListAsync);
        assets.MapGet("/{id:int}", GetAsync);
        assets.MapPost(string.Empty, CreateAsync)
            .Accepts<SaveAssetRequest>("application/json")
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));
        return endpoints;
    }

    private static async Task<Results<Ok<PagedResult<AssetListItem>>, ValidationProblem>> ListAsync(
        [AsParameters] AssetListRequest request, AssetService assets, CancellationToken cancellationToken)
    {
        var result = await assets.ListAsync(request, cancellationToken);
        return result.Page is { } page ? TypedResults.Ok(page) : ApiResults.ValidationProblem(result.Errors!);
    }

    private static async Task<Results<Ok<AssetDetails>, ProblemHttpResult>> GetAsync(int id, AssetService assets, CancellationToken cancellationToken) =>
        await assets.FindAsync(id, cancellationToken) is { } asset ? TypedResults.Ok(asset) : NotFound();

    private static async Task<IResult> CreateAsync(SaveAssetRequest body, AssetService assets, CancellationToken cancellationToken)
    {
        var result = await assets.CreateAsync(body, cancellationToken);
        return result is { Outcome: AssetWriteOutcome.Succeeded, Asset: { } asset }
            ? TypedResults.Created($"{Path}/{asset.Id}", asset)
            : Failure(result);
    }

    /// <summary>The response for a write that did not succeed.</summary>
    private static IResult Failure(AssetWriteResult result) => result.Outcome switch
    {
        AssetWriteOutcome.ValidationFailed => ApiResults.ValidationProblem(result.Errors!),
        AssetWriteOutcome.NotFound => NotFound(),
        AssetWriteOutcome.DuplicateValue => TypedResults.Problem(
            "Demirbaş kodu ve seri numarası her demirbaşta farklı olmalıdır; arşivlenmiş demirbaşlar da sayılır.",
            statusCode: StatusCodes.Status409Conflict,
            title: "Bu bilgiler başka bir demirbaşta kullanılıyor.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "duplicate_value",
                ["errors"] = ApiResults.CamelCaseKeys(result.Errors!),
            }),
        _ => AssetProblems.ForRule(result.RuleCode),
    };

    private static ProblemHttpResult NotFound() =>
        ApiResults.Problem(StatusCodes.Status404NotFound, "Demirbaş bulunamadı.", "Kayıt silinmiş veya adres yanlış olabilir.", "asset_not_found");
}
