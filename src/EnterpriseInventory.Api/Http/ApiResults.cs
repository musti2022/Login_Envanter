using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EnterpriseInventory.Api.Http;

/// <summary>Error responses the endpoints share: ProblemDetails with a Turkish title and a stable <c>code</c>.</summary>
internal static class ApiResults
{
    public static ProblemHttpResult Problem(int status, string title, string? detail, string code) =>
        TypedResults.Problem(detail, statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>Field errors keyed by the JSON property names the client sent (camelCase).</summary>
    public static ValidationProblem ValidationProblem(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(CamelCaseKeys(errors), title: "İstek geçersiz.");

    public static Dictionary<string, string[]> CamelCaseKeys(IDictionary<string, string[]> errors) =>
        errors.ToDictionary(e => JsonNamingPolicy.CamelCase.ConvertName(e.Key), e => e.Value);
}
