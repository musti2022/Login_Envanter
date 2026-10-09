using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Authentication;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnterpriseInventory.UnitTests.Authentication;

public class SignInHandlerTests
{
    private const string Password = "Correct-Horse-Battery-Staple-42";
    private static readonly DirectoryAccount Ayse = new(Guid.NewGuid(), "S-1-5-21-1-2-3-1105", "ayse.admin", "Ayşe Yılmaz");

    private readonly FakeDirectory _directory = new();
    private readonly RecordingSessions _sessions = new();
    private readonly ListLogger _logger = new();

    [Fact]
    public async Task Member_with_the_right_password_signs_in_and_is_recorded()
    {
        _directory.Result = DirectorySignInResult.Succeeded(Ayse);

        var result = await Handler().HandleAsync(new SignInRequest(" ayse.admin ", Password), CancellationToken.None);

        Assert.Equal(SignInOutcome.Succeeded, result.Outcome);
        Assert.Equal(Ayse, result.Account);
        Assert.Equal(RecordingSessions.Session, result.Session);
        Assert.Equal([Ayse], _sessions.Recorded);
        Assert.Equal(("ayse.admin", Password), _directory.LastCall);
        Assert.Contains(_logger.Messages, m => m.Level == LogLevel.Information && m.Text.Contains("ayse.admin signed in from 10.0.0.5", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DirectorySignInStatus.AccountDisabled, SignInOutcome.AccountUnavailable)]
    [InlineData(DirectorySignInStatus.AccountExpired, SignInOutcome.AccountUnavailable)]
    [InlineData(DirectorySignInStatus.PasswordExpired, SignInOutcome.AccountUnavailable)]
    [InlineData(DirectorySignInStatus.PasswordMustChange, SignInOutcome.AccountUnavailable)]
    [InlineData(DirectorySignInStatus.LogonNotPermitted, SignInOutcome.AccountUnavailable)]
    [InlineData(DirectorySignInStatus.NotAuthorized, SignInOutcome.NotAuthorized)]
    public async Task Refusals_after_the_password_was_accepted_name_the_user(DirectorySignInStatus status, SignInOutcome expected)
    {
        _directory.Result = DirectorySignInResult.Failed(status);

        var result = await Handler().HandleAsync(new SignInRequest("ayse.admin", Password), CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Account);
        Assert.Empty(_sessions.Recorded);
        Assert.Contains(_logger.Messages, m => m.Level >= LogLevel.Warning && m.Text.Contains("ayse.admin", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DirectorySignInStatus.InvalidCredentials, SignInOutcome.InvalidCredentials)]
    [InlineData(DirectorySignInStatus.DirectoryUnavailable, SignInOutcome.DirectoryUnavailable)]

    // AD reports a lockout whatever password was typed; telling the caller would confirm the account exists.
    [InlineData(DirectorySignInStatus.AccountLocked, SignInOutcome.InvalidCredentials)]
    public async Task Refusals_before_the_password_was_accepted_mask_the_user_name(DirectorySignInStatus status, SignInOutcome expected)
    {
        _directory.Result = DirectorySignInResult.Failed(status);

        var result = await Handler().HandleAsync(new SignInRequest("ayse.admin", Password), CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Empty(_sessions.Recorded);
        var entry = Assert.Single(_logger.Messages, m => m.Level >= LogLevel.Warning);
        Assert.Contains("ay… (10 characters)", entry.Text, StringComparison.Ordinal);
        Assert.Contains(status == DirectorySignInStatus.DirectoryUnavailable ? "directory is unavailable" : status.ToString(), entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ayse.admin", entry.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sign_in_that_cannot_be_recorded_is_refused()
    {
        _directory.Result = DirectorySignInResult.Succeeded(Ayse);
        _sessions.Failure = new InvalidOperationException("database down");

        var result = await Handler().HandleAsync(new SignInRequest("ayse.admin", Password), CancellationToken.None);

        Assert.Equal(SignInOutcome.SignInUnavailable, result.Outcome);
        Assert.Null(result.Account);
        Assert.Contains(_logger.Messages, m => m.Level == LogLevel.Error && m.Text.Contains("could not be recorded", StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Messages, m => m.Text.Contains("signed in from", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cancelled_sign_in_is_not_turned_into_a_refusal()
    {
        _directory.Result = DirectorySignInResult.Succeeded(Ayse);
        _sessions.Failure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Handler().HandleAsync(new SignInRequest("ayse.admin", Password), CancellationToken.None));
    }

    [Fact]
    public async Task A_success_without_an_account_is_treated_as_a_refusal()
    {
        _directory.Result = new DirectorySignInResult(DirectorySignInStatus.Succeeded);

        var result = await Handler().HandleAsync(new SignInRequest("ayse.admin", Password), CancellationToken.None);

        Assert.Equal(SignInOutcome.InvalidCredentials, result.Outcome);
        Assert.Empty(_sessions.Recorded);
    }

    [Theory]
    [InlineData(null, Password, "UserName", "Kullanıcı adı zorunludur.")]
    [InlineData("   ", Password, "UserName", "Kullanıcı adı zorunludur.")]
    [InlineData("ayse\r\nadmin", Password, "UserName", "Kullanıcı adı geçersiz karakter içeriyor.")]
    [InlineData("ayse.admin", null, "Password", "Parola zorunludur.")]
    [InlineData("ayse.admin", "", "Password", "Parola zorunludur.")]
    [InlineData("ayse.admin", "   ", "Password", "Parola zorunludur.")]
    public async Task Invalid_requests_never_reach_the_directory(string? userName, string? password, string field, string message)
    {
        var result = await Handler().HandleAsync(new SignInRequest(userName, password), CancellationToken.None);

        Assert.Equal(SignInOutcome.ValidationFailed, result.Outcome);
        Assert.Equal([message], result.Errors![field]);
        Assert.Null(_directory.LastCall);
    }

    [Fact]
    public async Task Overlong_input_is_refused()
    {
        var result = await Handler().HandleAsync(new SignInRequest(new string('a', 257), new string('p', 257)), CancellationToken.None);

        Assert.Equal(SignInOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(["Kullanıcı adı en fazla 256 karakter olabilir."], result.Errors!["UserName"]);
        Assert.Equal(["Parola en fazla 256 karakter olabilir."], result.Errors["Password"]);
    }

    [Theory]
    [InlineData(DirectorySignInStatus.Succeeded)]
    [InlineData(DirectorySignInStatus.InvalidCredentials)]
    [InlineData(DirectorySignInStatus.DirectoryUnavailable)]
    public async Task The_password_is_never_logged(DirectorySignInStatus status)
    {
        _directory.Result = status == DirectorySignInStatus.Succeeded ? DirectorySignInResult.Succeeded(Ayse) : DirectorySignInResult.Failed(status);

        await Handler().HandleAsync(new SignInRequest("ayse.admin", Password), CancellationToken.None);

        Assert.NotEmpty(_logger.Messages);
        Assert.DoesNotContain(_logger.Messages, m => m.Text.Contains(Password, StringComparison.Ordinal));
    }

    private SignInHandler Handler()
    {
        // The validator exactly as the application registers it.
        using var services = new ServiceCollection().AddApplication().BuildServiceProvider();
        var validator = services.GetRequiredService<IValidator<SignInRequest>>();
        return new SignInHandler(validator, _directory, _sessions, new FixedRequestContext(), _logger);
    }

    private sealed class FakeDirectory : IDirectoryService
    {
        public DirectorySignInResult Result { get; set; } = DirectorySignInResult.Failed(DirectorySignInStatus.InvalidCredentials);

        public (string UserName, string Password)? LastCall { get; private set; }

        public Task<DirectorySignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken)
        {
            LastCall = (userName, password);
            return Task.FromResult(Result);
        }

        public Task<DirectoryAccessStatus> CheckAccessAsync(Guid objectGuid, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingSessions : IUserSessionService
    {
        public static readonly StartedSession Session = new("session-key", DateTimeOffset.UnixEpoch);

        public List<DirectoryAccount> Recorded { get; } = [];

        public Exception? Failure { get; set; }

        public Task<StartedSession> StartAsync(DirectoryAccount account, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException<StartedSession>(Failure);
            }

            Recorded.Add(account);
            return Task.FromResult(Session);
        }

        public Task<SessionValidationResult> ValidateAsync(string sessionKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SessionValidationResult> CheckAsync(string sessionKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task EndAsync(string sessionKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedRequestContext : IRequestContext
    {
        public string CorrelationId => "test-correlation";

        public string? ClientAddress => "10.0.0.5";
    }

    private sealed class ListLogger : ILogger<SignInHandler>
    {
        public List<(LogLevel Level, string Text)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add((logLevel, formatter(state, exception) + " " + string.Join(' ', (state as IEnumerable<KeyValuePair<string, object?>>)?.Select(p => p.Value) ?? [])));
    }
}
