namespace EnterpriseInventory.Application.Abstractions;

/// <summary>The signed-in user on whose behalf the current operation runs.</summary>
public interface ICurrentUser
{
    /// <summary>Directory user name of the signed-in user, or <c>null</c> when nobody is signed in.</summary>
    string? UserName { get; }
}
