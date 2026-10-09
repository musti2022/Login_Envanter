namespace EnterpriseInventory.Infrastructure;

/// <summary>Tags that group health checks into the API's health endpoints.</summary>
public static class HealthCheckTags
{
    /// <summary>Dependencies the application needs to serve requests (database, later Active Directory).</summary>
    public const string Ready = "ready";
}
