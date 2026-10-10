namespace EnterpriseInventory.Application.Lookups;

public enum LookupWriteOutcome
{
    Succeeded = 0,

    /// <summary>The input is wrong, or the brand or city it names is missing or inactive; see <c>Errors</c>.</summary>
    ValidationFailed = 1,

    /// <summary>The name is taken (ignoring case), by an active or an inactive lookup.</summary>
    DuplicateValue = 2,

    /// <summary>No lookup of that kind has the ID.</summary>
    NotFound = 3,

    /// <summary>The lookup was changed by someone else after the caller read it; nothing was saved.</summary>
    ConcurrencyConflict = 4,
}

/// <summary>What happened to a create or an update. <see cref="Item"/> is the lookup as saved.</summary>
public sealed record LookupWriteResult<T>(LookupWriteOutcome Outcome, T? Item, IDictionary<string, string[]>? Errors)
    where T : class;

public static class LookupWriteResult
{
    public static LookupWriteResult<T> Succeeded<T>(T item)
        where T : class => new(LookupWriteOutcome.Succeeded, item, null);

    public static LookupWriteResult<T> Invalid<T>(IDictionary<string, string[]> errors)
        where T : class => new(LookupWriteOutcome.ValidationFailed, null, errors);

    public static LookupWriteResult<T> Duplicate<T>(string message)
        where T : class => new(LookupWriteOutcome.DuplicateValue, null, new Dictionary<string, string[]> { [nameof(CreateLookupRequest.Name)] = [message] });

    public static LookupWriteResult<T> NotFound<T>()
        where T : class => new(LookupWriteOutcome.NotFound, null, null);

    public static LookupWriteResult<T> Conflict<T>()
        where T : class => new(LookupWriteOutcome.ConcurrencyConflict, null, null);
}

/// <summary>Turkish messages for checks that need the database.</summary>
public static class LookupMessages
{
    public const string BrandNotFound = "Seçilen marka bulunamadı.";
    public const string BrandInactive = "Seçilen marka pasif; yeni seçimlerde kullanılamaz.";
    public const string CityNotFound = "Seçilen şehir bulunamadı.";
    public const string CityInactive = "Seçilen şehir pasif; yeni seçimlerde kullanılamaz.";
    public const string ModelTaken = "Bu markada aynı adla bir model zaten var.";
    public const string LocationTaken = "Bu şehirde aynı adla bir lokasyon zaten var.";
    public const string ModelBrandInactive = "Markası pasif olan model etkinleştirilemez; önce markayı etkinleştirin.";
    public const string LocationCityInactive = "Şehri pasif olan lokasyon etkinleştirilemez; önce şehri etkinleştirin.";

    public static string NameTaken(LookupKind kind) => kind switch
    {
        LookupKind.Brand => "Aynı adla bir marka zaten var.",
        LookupKind.City => "Aynı adla bir şehir zaten var.",
        _ => "Aynı adla bir departman zaten var.",
    };
}
