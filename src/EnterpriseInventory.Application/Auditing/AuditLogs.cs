using System.Globalization;
using System.Text.Json;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Domain.Users;
using FluentValidation;

namespace EnterpriseInventory.Application.Auditing;

/// <summary>
/// Query string of <c>GET /api/audit-logs</c>, as typed by the caller; checked by <see cref="AuditLogRequestValidator"/>.
/// Every filter given must match (AND); <c>action</c> may be given several times and matches any of them.
/// </summary>
public sealed record AuditLogRequest
{
    public const int DefaultPageSize = 25;
    public const int TextMaxLength = 100;

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    /// <summary>The kind of record changed: one of <see cref="AuditedEntities.Names"/>.</summary>
    public string? EntityName { get; init; }

    /// <summary>The changed record's ID; only together with <see cref="EntityName"/>.</summary>
    public string? EntityId { get; init; }

    /// <summary>Part of an asset code (case and accents ignored): the records of the assets whose code contains it.</summary>
    public string? AssetCode { get; init; }

    /// <summary><see cref="AuditAction"/> names, e.g. <c>Updated</c>.</summary>
    public string[]? Action { get; init; }

    /// <summary>Part of the user name of whoever made the change (case ignored).</summary>
    public string? UserName { get; init; }

    /// <summary>Records written at or after this moment: ISO 8601 with an offset, e.g. <c>2026-10-01T00:00:00+03:00</c>.</summary>
    public string? From { get; init; }

    /// <summary>Records written before this moment (exclusive), in the same format as <see cref="From"/>.</summary>
    public string? To { get; init; }

    /// <summary>The correlation ID of one request: every record that request wrote.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>The kinds of record the audit log covers, by the names it stores.</summary>
public static class AuditedEntities
{
    public static readonly IReadOnlyList<string> Names =
    [
        nameof(Asset),
        nameof(Brand),
        nameof(AssetModel),
        nameof(City),
        nameof(Department),
        nameof(Location),
        nameof(AdminUser),
    ];

    /// <summary>The stored spelling of <paramref name="name"/>, ignoring case, or <c>null</c> when it is not one of <see cref="Names"/>.</summary>
    public static string? Find(string? name) => Names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A validated audit log request.</summary>
public sealed record AuditLogCriteria(int Page, int PageSize)
{
    public string? EntityName { get; init; }

    public string? EntityId { get; init; }

    public string? AssetCode { get; init; }

    public IReadOnlyList<AuditAction> Actions { get; init; } = [];

    public string? UserName { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public string? CorrelationId { get; init; }

    public static AuditLogCriteria FromRequest(AuditLogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AuditLogCriteria(request.Page ?? 1, request.PageSize ?? AuditLogRequest.DefaultPageSize)
        {
            EntityName = AuditedEntities.Find(request.EntityName),
            EntityId = Text(request.EntityId),
            AssetCode = Text(request.AssetCode),
            Actions = (request.Action ?? []).Select(EnumNames.Parse<AuditAction>).Distinct().ToList(),
            UserName = Text(request.UserName),
            From = AuditLogRequestValidator.ParseMoment(request.From),
            To = AuditLogRequestValidator.ParseMoment(request.To),
            CorrelationId = Text(request.CorrelationId),
        };
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// One audit record: what was changed (<see cref="EntityName"/>, <see cref="EntityId"/>), how, by whom, when, in which
/// request, and the fields it changed as JSON objects (<c>null</c> where there were none).
/// </summary>
/// <param name="EntityLabel">
/// How the record is known now: an asset's code, a lookup's name, an administrator's user name; <c>null</c> when it
/// no longer exists.
/// </param>
public sealed record AuditLogEntry(
    long Id,
    string EntityName,
    string EntityId,
    string? EntityLabel,
    AuditAction Action,
    string UserName,
    DateTimeOffset Timestamp,
    string CorrelationId,
    JsonElement? OldValues,
    JsonElement? NewValues);

/// <summary>A page of audit records, or the reasons the request was refused.</summary>
public sealed record AuditLogListResult(PagedResult<AuditLogEntry>? Page, IDictionary<string, string[]>? Errors);

/// <summary>Reads the audit log. Records are never changed or deleted through the application.</summary>
public interface IAuditLogStore
{
    /// <summary>The records that match, newest first (by the order they were written in).</summary>
    Task<PagedResult<AuditLogEntry>> ListAsync(AuditLogCriteria criteria, CancellationToken cancellationToken);

    Task<AuditLogEntry?> FindAsync(long id, CancellationToken cancellationToken);
}

/// <summary>The audit log screen's use cases: checks the caller's filters and reads the records.</summary>
public sealed class AuditLogService(IValidator<AuditLogRequest> validator, IAuditLogStore store)
{
    public async Task<AuditLogListResult> ListAsync(AuditLogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return validation.IsValid
            ? new AuditLogListResult(await store.ListAsync(AuditLogCriteria.FromRequest(request), cancellationToken).ConfigureAwait(false), null)
            : new AuditLogListResult(null, validation.ToDictionary());
    }

    public Task<AuditLogEntry?> FindAsync(long id, CancellationToken cancellationToken) => store.FindAsync(id, cancellationToken);
}

internal sealed class AuditLogRequestValidator : AbstractValidator<AuditLogRequest>
{
    public AuditLogRequestValidator()
    {
        RuleFor(r => r.Page).MustBePage();
        RuleFor(r => r.PageSize).MustBePageSize();

        RuleFor(r => r.EntityName)
            .Must(name => AuditedEntities.Find(name) is not null)
            .When(r => !string.IsNullOrWhiteSpace(r.EntityName))
            .WithMessage($"Kayıt türü geçersiz. Geçerli değerler: {string.Join(", ", AuditedEntities.Names)}.");

        RuleFor(r => r.EntityId)
            .Cascade(CascadeMode.Stop)
            .Must(PlainText(AuditLog.EntityIdMaxLength)).WithMessage($"Kayıt kimliği en fazla {AuditLog.EntityIdMaxLength} karakter olabilir ve kontrol karakteri içeremez.")
            .Must((request, _) => !string.IsNullOrWhiteSpace(request.EntityName)).WithMessage("Kayıt kimliği yalnızca kayıt türüyle birlikte aranabilir.")
            .When(r => !string.IsNullOrWhiteSpace(r.EntityId));

        RuleFor(r => r.AssetCode)
            .Cascade(CascadeMode.Stop)
            .Must(PlainText(Asset.AssetCodeMaxLength)).WithMessage($"Demirbaş kodu en fazla {Asset.AssetCodeMaxLength} karakter olabilir ve kontrol karakteri içeremez.")
            .Must((request, _) => string.IsNullOrWhiteSpace(request.EntityName) || AuditedEntities.Find(request.EntityName) == nameof(Asset))
            .WithMessage("Demirbaş kodu yalnızca demirbaş kayıtlarında aranabilir.")
            .When(r => !string.IsNullOrWhiteSpace(r.AssetCode));

        RuleFor(r => r.Action)
            .Must(names => names is null || names.All(EnumNames.IsDefined<AuditAction>))
            .WithMessage($"İşlem filtresi geçersiz. Geçerli değerler: {string.Join(", ", Enum.GetNames<AuditAction>())}.");

        RuleFor(r => r.UserName)
            .Must(PlainText(AuditableEntity.UserNameMaxLength))
            .When(r => !string.IsNullOrWhiteSpace(r.UserName))
            .WithMessage($"Kullanıcı adı en fazla {AuditableEntity.UserNameMaxLength} karakter olabilir ve kontrol karakteri içeremez.");

        RuleFor(r => r.From)
            .Must(text => ParseMoment(text) is not null)
            .When(r => !string.IsNullOrWhiteSpace(r.From))
            .WithMessage("Başlangıç zamanı geçersiz. Saat dilimiyle birlikte ISO 8601 biçiminde olmalıdır (ör. 2026-10-01T00:00:00+03:00).");

        RuleFor(r => r.To)
            .Cascade(CascadeMode.Stop)
            .Must(text => ParseMoment(text) is not null)
            .WithMessage("Bitiş zamanı geçersiz. Saat dilimiyle birlikte ISO 8601 biçiminde olmalıdır (ör. 2026-10-02T00:00:00+03:00).")
            .Must((request, text) => ParseMoment(request.From) is not { } from || from < ParseMoment(text))
            .WithMessage("Bitiş zamanı başlangıç zamanından sonra olmalıdır.")
            .When(r => !string.IsNullOrWhiteSpace(r.To));

        RuleFor(r => r.CorrelationId)
            .Must(PlainText(AuditLog.CorrelationIdMaxLength))
            .When(r => !string.IsNullOrWhiteSpace(r.CorrelationId))
            .WithMessage($"İşlem numarası (correlation ID) en fazla {AuditLog.CorrelationIdMaxLength} karakter olabilir ve kontrol karakteri içeremez.");
    }

    /// <summary>
    /// A moment with an explicit offset (<c>Z</c> or <c>+03:00</c>), or <c>null</c>. Without an offset the server's own
    /// time zone would decide which day "2026-10-01T00:00" is, so such values are refused.
    /// </summary>
    public static DateTimeOffset? ParseMoment(string? text)
    {
        var value = text?.Trim();
        return value is { Length: > 0 }
            && HasExplicitOffset(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
                ? moment
                : null;
    }

    private static bool HasExplicitOffset(string value) =>
        value.IndexOf('T', StringComparison.OrdinalIgnoreCase) > 0
        && (value[^1] is 'Z' or 'z' || (value.Length > 6 && value[^6] is '+' or '-' && value[^3] == ':'));

    private static Func<string?, bool> PlainText(int maxLength) =>
        text => text is null || (text.Trim().Length <= maxLength && !text.Any(char.IsControl));
}
