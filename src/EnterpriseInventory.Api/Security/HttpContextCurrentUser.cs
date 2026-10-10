using EnterpriseInventory.Application.Abstractions;

namespace EnterpriseInventory.Api.Security;

/// <summary>Reads the signed-in user from the current HTTP request; <c>null</c> for anonymous requests.</summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public string? UserName
    {
        get
        {
            var identity = httpContextAccessor.HttpContext?.User.Identity;
            return identity is { IsAuthenticated: true } ? identity.Name : null;
        }
    }
}
