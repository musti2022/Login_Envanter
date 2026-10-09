using FluentValidation;

namespace EnterpriseInventory.Application.Employees;

/// <summary>Query string of <c>GET /api/employees/search</c>.</summary>
public sealed record EmployeeSearchRequest
{
    public const int MinLength = 2;
    public const int MaxLength = 64;
    public const int MaxWords = 4;

    /// <summary>The start of a name, logon name or e-mail address; several words narrow the search.</summary>
    public string? Q { get; init; }
}

/// <summary>A directory person who can be chosen as the holder of an asset.</summary>
/// <param name="ObjectGuid">What an assignment sends back to name the person.</param>
public sealed record EmployeeSearchItem(Guid ObjectGuid, string UserName, string DisplayName, string? Email, string? Department, string? Title);

/// <param name="HasMore">More people matched than the list holds; typing more of the name narrows it.</param>
public sealed record EmployeeSearchResponse(IReadOnlyList<EmployeeSearchItem> Items, bool HasMore);

/// <summary>The people found, the reasons the request was refused, or neither when the directory is unavailable.</summary>
public sealed record EmployeeSearchResult(EmployeeSearchResponse? Response, IDictionary<string, string[]>? Errors)
{
    public bool DirectoryUnavailable => Response is null && Errors is null;
}

internal sealed class EmployeeSearchRequestValidator : AbstractValidator<EmployeeSearchRequest>
{
    public EmployeeSearchRequestValidator()
    {
        RuleFor(r => r.Q)
            .Cascade(CascadeMode.Stop)
            .Must(q => !string.IsNullOrWhiteSpace(q)).WithMessage("Aranacak adı yazın.")
            .Must(q => q!.Trim().Length >= EmployeeSearchRequest.MinLength)
            .WithMessage($"Aramak için en az {EmployeeSearchRequest.MinLength} karakter yazın.")
            .Must(q => q!.Trim().Length <= EmployeeSearchRequest.MaxLength)
            .WithMessage($"Arama en fazla {EmployeeSearchRequest.MaxLength} karakter olabilir.")
            .Must(q => !q!.Any(char.IsControl)).WithMessage("Arama geçersiz karakter içeriyor.")
            .Must(q => EmployeeService.Words(q).Count <= EmployeeSearchRequest.MaxWords)
            .WithMessage($"Arama en fazla {EmployeeSearchRequest.MaxWords} kelime olabilir.");
    }
}

/// <summary>
/// Finds employees in the directory for the assignment screen. Any enabled account can be found, whether or not
/// it may sign in to the application: the people who hold assets are not the people who manage them.
/// </summary>
public sealed class EmployeeService(IValidator<EmployeeSearchRequest> validator, IEmployeeDirectory directory)
{
    /// <summary>Enough to pick from; more than this means the search should be narrowed.</summary>
    public const int MaxResults = 20;

    public async Task<EmployeeSearchResult> SearchAsync(EmployeeSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new EmployeeSearchResult(null, validation.ToDictionary());
        }

        var found = await directory.SearchAsync(string.Join(' ', Words(request.Q)), MaxResults, cancellationToken).ConfigureAwait(false);
        if (found.Status != DirectoryLookupStatus.Succeeded)
        {
            return new EmployeeSearchResult(null, null);
        }

        var items = found.People
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.DisplayName, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true))
            .ThenBy(p => p.SamAccountName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .Select(p => new EmployeeSearchItem(p.ObjectGuid, p.SamAccountName, p.DisplayName, p.Email, p.Department, p.Title))
            .ToList();
        return new EmployeeSearchResult(new EmployeeSearchResponse(items, found.HasMore), null);
    }

    /// <summary>The words of a search, split on any whitespace.</summary>
    internal static IReadOnlyList<string> Words(string? text) =>
        text is null ? [] : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
