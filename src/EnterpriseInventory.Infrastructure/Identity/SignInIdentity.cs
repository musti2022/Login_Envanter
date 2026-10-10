using EnterpriseInventory.Application.Abstractions;

namespace EnterpriseInventory.Infrastructure.Identity;

/// <summary>
/// During a sign-in the request has no signed-in user yet, but the person has just proved who they are. This
/// lets the sign-in record be saved under their name while the "no anonymous writes" rule of
/// <see cref="Persistence.Interceptors.AuditableEntityInterceptor"/> stays in force for everything else.
/// </summary>
internal sealed class SignInIdentity
{
    public string? UserName { get; private set; }

    /// <summary>Acts as <paramref name="userName"/> until the returned scope is disposed.</summary>
    public IDisposable ActAs(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        if (UserName is not null)
        {
            throw new InvalidOperationException("A sign-in is already in progress in this scope.");
        }

        UserName = userName;
        return new Scope(this);
    }

    private sealed class Scope(SignInIdentity owner) : IDisposable
    {
        public void Dispose() => owner.UserName = null;
    }
}

/// <summary>The signed-in user, or the person whose sign-in is being recorded.</summary>
internal sealed class SignInAwareCurrentUser(ICurrentUser currentUser, SignInIdentity signIn) : ICurrentUser
{
    public string? UserName => signIn.UserName ?? currentUser.UserName;
}
