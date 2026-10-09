namespace EnterpriseInventory.Api.Security;

internal static class AppClaimTypes
{
    public const string DisplayName = "ei:display_name";

    /// <summary>The key of the server-side session; the cookie is worthless once that session has ended.</summary>
    public const string SessionKey = "ei:session";
}
