using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Application.Assets;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EnterpriseInventory.Api.Assets;

/// <summary>
/// <c>/api/assets</c>. Every endpoint needs a signed-in Administrator (the fallback policy); state-changing ones
/// also need the CSRF token (see CsrfProtectionMiddleware).
/// </summary>
internal static class AssetEndpoints
{
    public const string Path = "/api/assets";

    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var assets = endpoints.MapGroup(Path);
        assets.MapGet(string.Empty, ListAsync);
        assets.MapGet("/{id:int}", GetAsync);
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

    private static ProblemHttpResult NotFound() =>
        ApiResults.Problem(StatusCodes.Status404NotFound, "Demirbaş bulunamadı.", "Kayıt silinmiş veya adres yanlış olabilir.", "asset_not_found");
}
