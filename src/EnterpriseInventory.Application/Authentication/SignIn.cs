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

    /// <summary>
    /// The account is disabled or expired, its password must be changed, or logon restrictions apply. The
    /// directory only says so once the password was right. A locked account is reported as
    /// <see cref="InvalidCredentials"/> instead, because AD reports lockout whatever password was typed.
    /// </summary>
    AccountUnavailable = 3,

    /// <summary>The user is not a member of the allowed group.</summary>
    NotAuthorized = 4,

    /// <summary>The directory could not be used, so the sign-in failed closed.</summary>
    DirectoryUnavailable = 5,

    /// <summary>The directory accepted the user but the sign-in could not be recorded, so no session was started.</summary>
    SignInUnavailable = 6,
}

/// <param name="Session">The server-side session started for a successful sign-in.</param>
public sealed record SignInResult(
    SignInOutcome Outcome,
    DirectoryAccount? Account = null,
    IDictionary<string, string[]>? Errors = null,
    StartedSession? Session = null);

/// <summary>
/// Signs a user in against the directory and starts a server-side session. Logs say who tried and why it
/// failed; the password never appears in a log, an exception or the database. The typed user name is logged in
/// full only once the directory has accepted the password: before that it may be a mistyped password, so only its
/// first characters and length are kept.
/// </summary>
public sealed partial class SignInHandler(
    IValidator<SignInRequest> validator,
    IDirectoryService directory,
    IUserSessionService sessions,
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
            StartedSession session;
            try
            {
                session = await sessions.StartAsync(account, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A sign-in that cannot be recorded (and audited) does not start a session.
                LogRecordingFailed(ex, account.SamAccountName, requestContext.ClientAddress);
                return new SignInResult(SignInOutcome.SignInUnavailable);
            }

            LogSignedIn(account.SamAccountName, requestContext.ClientAddress);
            return new SignInResult(SignInOutcome.Succeeded, account, Session: session);
        }

        var outcome = result.Status switch
        {
            DirectorySignInStatus.AccountDisabled or DirectorySignInStatus.AccountExpired
                or DirectorySignInStatus.PasswordExpired or DirectorySignInStatus.PasswordMustChange
                or DirectorySignInStatus.LogonNotPermitted => SignInOutcome.AccountUnavailable,
            DirectorySignInStatus.NotAuthorized => SignInOutcome.NotAuthorized,
            DirectorySignInStatus.DirectoryUnavailable => SignInOutcome.DirectoryUnavailable,

            // Anything else, including a lockout and a "success" without an account, is a refusal.
            _ => SignInOutcome.InvalidCredentials,
        };

        // Only these outcomes mean the directory accepted the password.
        var passwordProven = outcome is SignInOutcome.AccountUnavailable or SignInOutcome.NotAuthorized;
        var loggedName = passwordProven ? userName : MaskUserName(userName);
        if (outcome == SignInOutcome.DirectoryUnavailable)
        {
            LogDirectoryUnavailable(loggedName, requestContext.ClientAddress);
        }
        else
        {
            LogSignInRefused(loggedName, result.Status, requestContext.ClientAddress);
        }

        return new SignInResult(outcome);
    }

    /// <summary>E.g. <c>ay… (10 characters)</c>: enough to match a complaint to a log entry, too little to be a password.</summary>
    private static string MaskUserName(string userName) =>
        $"{userName[..Math.Min(2, userName.Length)]}… ({userName.Length} characters)";

    [LoggerMessage(Level = LogLevel.Information, Message = "{UserName} signed in from {ClientAddress}")]
    private partial void LogSignedIn(string userName, string? clientAddress);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in refused for {UserName} from {ClientAddress}: {Reason}")]
    private partial void LogSignInRefused(string userName, DirectorySignInStatus reason, string? clientAddress);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sign-in for {UserName} from {ClientAddress} failed closed: the directory is unavailable")]
    private partial void LogDirectoryUnavailable(string userName, string? clientAddress);

    [LoggerMessage(Level = LogLevel.Error, Message = "{UserName} was accepted by the directory but the sign-in from {ClientAddress} could not be recorded; no session was started")]
    private partial void LogRecordingFailed(Exception exception, string userName, string? clientAddress);
}
