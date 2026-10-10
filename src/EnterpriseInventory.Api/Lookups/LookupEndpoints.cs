using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Application.Lookups;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseInventory.Api.Lookups;

/// <summary>
/// <c>/api/brands</c>, <c>/api/models</c>, <c>/api/cities</c>, <c>/api/locations</c> and <c>/api/departments</c>:
/// the lists the filters and forms choose from, adding to them, and renaming, deactivating or reactivating
/// (<c>PUT /api/brands/{id}</c>...). Signed-in Administrators only (the fallback policy); POST and PUT also need the
/// CSRF token. Lookups are never deleted, so there is no DELETE.
/// </summary>
internal static class LookupEndpoints
{
    /// <summary>A name is at most 100 characters; anything near this limit is not a lookup.</summary>
    private const long MaxBodyBytes = 4 * 1024;

    public static IEndpointRouteBuilder MapLookupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapSimple(endpoints, "/api/brands", LookupKind.Brand);
        MapSimple(endpoints, "/api/cities", LookupKind.City);
        MapSimple(endpoints, "/api/departments", LookupKind.Department);

        endpoints.MapGet("/api/models", ListModelsAsync);
        endpoints.MapPost("/api/models", CreateModelAsync)
            .Accepts<CreateModelRequest>("application/json")
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));
        endpoints.MapPut("/api/models/{id:int}", async (int id, UpdateLookupRequest body, LookupService lookups, CancellationToken cancellationToken) =>
                Updated(await lookups.UpdateModelAsync(id, body, cancellationToken)))
            .Accepts<UpdateLookupRequest>("application/json")
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));
        endpoints.MapGet("/api/locations", ListLocationsAsync);
        endpoints.MapPost("/api/locations", CreateLocationAsync)
            .Accepts<CreateLocationRequest>("application/json")
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));
        endpoints.MapPut("/api/locations/{id:int}", async (int id, UpdateLookupRequest body, LookupService lookups, CancellationToken cancellationToken) =>
                Updated(await lookups.UpdateLocationAsync(id, body, cancellationToken)))
            .Accepts<UpdateLookupRequest>("application/json")
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));
        return endpoints;
    }

    private static void MapSimple(IEndpointRouteBuilder endpoints, string path, LookupKind kind)
    {
        endpoints.MapGet(path, async (LookupService lookups, CancellationToken cancellationToken) =>
            TypedResults.Ok(await lookups.ListAsync(kind, cancellationToken)));
        endpoints.MapPost(path, async (CreateLookupRequest body, LookupService lookups, CancellationToken cancellationToken) =>
                Created(await lookups.CreateAsync(kind, body, cancellationToken)))
            .Accepts<CreateLookupRequest>("application/json")
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));
        endpoints.MapPut($"{path}/{{id:int}}", async (int id, UpdateLookupRequest body, LookupService lookups, CancellationToken cancellationToken) =>
                Updated(await lookups.UpdateAsync(kind, id, body, cancellationToken)))
            .Accepts<UpdateLookupRequest>("application/json")
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));
    }

    private static async Task<Results<Ok<IReadOnlyList<ModelItem>>, ValidationProblem>> ListModelsAsync(
        int? brandId, LookupService lookups, CancellationToken cancellationToken) =>
        brandId <= 0
            ? ApiResults.ValidationProblem(new Dictionary<string, string[]> { ["brandId"] = ["Marka filtresi geçersiz."] })
            : TypedResults.Ok(await lookups.ListModelsAsync(brandId, cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<LocationItem>>, ValidationProblem>> ListLocationsAsync(
        int? cityId, LookupService lookups, CancellationToken cancellationToken) =>
        cityId <= 0
            ? ApiResults.ValidationProblem(new Dictionary<string, string[]> { ["cityId"] = ["Şehir filtresi geçersiz."] })
            : TypedResults.Ok(await lookups.ListLocationsAsync(cityId, cancellationToken));

    private static async Task<IResult> CreateModelAsync(CreateModelRequest body, LookupService lookups, CancellationToken cancellationToken) =>
        Created(await lookups.CreateModelAsync(body, cancellationToken));

    private static async Task<IResult> CreateLocationAsync(CreateLocationRequest body, LookupService lookups, CancellationToken cancellationToken) =>
        Created(await lookups.CreateLocationAsync(body, cancellationToken));

    /// <summary><c>201</c> with the new lookup, or why it was refused. There is no single-lookup URL to point to.</summary>
    private static IResult Created<T>(LookupWriteResult<T> result)
        where T : class => result is { Outcome: LookupWriteOutcome.Succeeded, Item: { } item }
            ? TypedResults.Created((string?)null, item)
            : Failure(result);

    /// <summary><c>200</c> with the lookup as saved, or why the change was refused.</summary>
    private static IResult Updated<T>(LookupWriteResult<T> result)
        where T : class => result is { Outcome: LookupWriteOutcome.Succeeded, Item: { } item }
            ? TypedResults.Ok(item)
            : Failure(result);

    private static IResult Failure<T>(LookupWriteResult<T> result)
        where T : class => result.Outcome switch
        {
            LookupWriteOutcome.DuplicateValue => TypedResults.Problem(
                "Pasif kayıtlar da sayılır; büyük/küçük harf farkı ayrı ad sayılmaz.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Bu ad zaten kullanılıyor.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "duplicate_value",
                    ["errors"] = ApiResults.CamelCaseKeys(result.Errors!),
                }),
            LookupWriteOutcome.NotFound => ApiResults.Problem(
                StatusCodes.Status404NotFound, "Tanım bulunamadı.", "Adres yanlış olabilir.", "lookup_not_found"),
            LookupWriteOutcome.ConcurrencyConflict => ApiResults.Problem(
                StatusCodes.Status409Conflict,
                "Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi.",
                "Değişiklikleriniz kaydedilmedi. Kaydı yeniden açıp güncel bilgiler üzerinde tekrar deneyin.",
                "concurrency_conflict"),
            _ => ApiResults.ValidationProblem(result.Errors!),
        };
}
