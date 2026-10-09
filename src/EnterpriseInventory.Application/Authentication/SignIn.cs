using EnterpriseInventory.Application.Abstractions;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace EnterpriseInventory.Application.Authentication;

public sealed record SignInRequest(string? UserName, string? Password);

internal sealed class SignInRequestValidator : AbstractValidator<SignInRequest>
{
    /// <summary>Enough for a user principal name such as user@corp.example.com.</summary>
    public const int UserNameMaxLength = 256;

    /// <summary>Active Directory's own password length limit.</summary>
    public const int PasswordMaxLength = 256;

    public SignInRequestValidator()
    {
        RuleFor(r => r.UserName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Kullanıcı adı zorunludur.")
            .MaximumLength(UserNameMaxLength).WithMessage($"Kullanıcı adı en fazla {UserNameMaxLength} karakter olabilir.")
            .Must(name => !name!.Any(char.IsControl)).WithMessage("Kullanıcı adı geçersiz karakter içeriyor.");

        // An empty password must never reach the directory: LDAP treats a bind with an empty password as an
        // anonymous ("unauthenticated") bind, which some servers accept.
        RuleFor(r => r.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Parola zorunludur.")
            .MaximumLength(PasswordMaxLength).WithMessage($"Parola en fazla {PasswordMaxLength} karakter olabilir.");
    }
}

public enum SignInOutcome
{
    Succeeded = 0,
    ValidationFailed = 1,
    InvalidCredentials = 2,

    /// <summary>The account is disabled, locked or expired, or its password must be changed.</summary>
    AccountUnavailable = 3,

    /// <summary>The user is not a member of the allowed group.</summary>
    NotAuthorized = 4,

    /// <summary>The directory could not be used, so the sign-in failed closed.</summary>
    DirectoryUnavailable = 5,
}

public sealed record SignInResult(
    SignInOutcome Outcome,
    DirectoryAccount? Account = null,
    IDictionary<string, string[]>? Errors = null);

/// <summary>
/// Signs a user in against the directory and records the administrator's sign-in. Logs say who tried and why it
/// failed; the password never appears in a log, an exception or the database.
/// </summary>
public sealed partial class SignInHandler(
    IValidator<SignInRequest> validator,
    IDirectoryService directory,
    IAdminUserStore adminUsers,
    IRequestContext requestContext,
    ILogger<SignInHandler> logger)
{
    public async Task<SignInResult> HandleAsync(SignInRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new SignInResult(SignInOutcome.ValidationFailed, Errors: validation.ToDictionary());
        }

        var userName = request.UserName!.Trim();
        var result = await directory.SignInAsync(userName, request.Password!, cancellationToken).ConfigureAwait(false);

        if (result is { Status: DirectorySignInStatus.Succeeded, Account: { } account })
        {
            await adminUsers.RecordSignInAsync(account, cancellationToken).ConfigureAwait(false);
            LogSignedIn(account.SamAccountName, requestContext.ClientAddress);
            return new SignInResult(SignInOutcome.Succeeded, account);
        }

        var outcome = result.Status switch
        {
            DirectorySignInStatus.AccountDisabled or DirectorySignInStatus.AccountLocked or DirectorySignInStatus.AccountExpired
                or DirectorySignInStatus.PasswordExpired or DirectorySignInStatus.PasswordMustChange
                or DirectorySignInStatus.LogonNotPermitted => SignInOutcome.AccountUnavailable,
            DirectorySignInStatus.NotAuthorized => SignInOutcome.NotAuthorized,
            DirectorySignInStatus.DirectoryUnavailable => SignInOutcome.DirectoryUnavailable,

            // Anything else, including a "success" without an account, is a refusal.
            _ => SignInOutcome.InvalidCredentials,
        };

        if (outcome == SignInOutcome.DirectoryUnavailable)
        {
            LogDirectoryUnavailable(userName, requestContext.ClientAddress);
        }
        else
        {
            LogSignInRefused(userName, result.Status, requestContext.ClientAddress);
        }

        return new SignInResult(outcome);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{UserName} signed in from {ClientAddress}")]
    private partial void LogSignedIn(string userName, string? clientAddress);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in refused for {UserName} from {ClientAddress}: {Reason}")]
    private partial void LogSignInRefused(string userName, DirectorySignInStatus reason, string? clientAddress);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sign-in for {UserName} from {ClientAddress} failed closed: the directory is unavailable")]
    private partial void LogDirectoryUnavailable(string userName, string? clientAddress);
}
