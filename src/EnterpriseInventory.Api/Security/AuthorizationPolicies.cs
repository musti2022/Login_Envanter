namespace EnterpriseInventory.Api.Security;

public static class Roles
{
    /// <summary>Given to every signed-in member of the allowed AD group (Bim_Envanter).</summary>
    public const string Administrator = "Administrator";
}

public static class AuthorizationPolicies
{
    /// <summary>Signed in with the <see cref="Roles.Administrator"/> role. Also the default and fallback policy.</summary>
    public const string Administrator = "Administrator";
}
