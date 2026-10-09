namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// Connection and access settings for the on-premises Active Directory, from the <c>ActiveDirectory</c>
/// configuration section. Real values (and the service account password) come from the server's configuration
/// or secret store, never from the repository; <see cref="ActiveDirectoryOptionsValidator"/> refuses to start
/// with missing, placeholder or insecure values.
/// </summary>
public sealed class ActiveDirectoryOptions
{
    public const string SectionName = "ActiveDirectory";

    public DirectoryMode Mode { get; set; } = DirectoryMode.Ldap;

    /// <summary>DNS name of the domain, e.g. <c>corp.example.com</c>. Users sign in as <c>user@Domain</c>.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>DNS name of the domain controller; the LDAPS certificate must be issued for this name.</summary>
    public string ServerFqdn { get; set; } = string.Empty;

    public int Port { get; set; } = 636;

    /// <summary>Must stay <c>true</c>: passwords are only ever sent inside a verified TLS connection.</summary>
    public bool UseLdaps { get; set; } = true;

    /// <summary>Where users are searched, e.g. <c>DC=corp,DC=example,DC=com</c> or an OU inside the domain.</summary>
    public string BaseDn { get; set; } = string.Empty;

    /// <summary>For messages and documentation only; access is decided by <see cref="AllowedGroupSid"/>.</summary>
    public string AllowedGroupName { get; set; } = "Bim_Envanter";

    /// <summary>objectSid of the Bim_Envanter security group, e.g. <c>S-1-5-21-…-1105</c>.</summary>
    public string AllowedGroupSid { get; set; } = string.Empty;

    /// <summary>Has no default on purpose: whether nested groups grant access must be decided explicitly.</summary>
    public NestedGroupPolicy? NestedGroupPolicy { get; set; }

    public string ServiceAccountUserName { get; set; } = string.Empty;

    public string ServiceAccountPassword { get; set; } = string.Empty;

    /// <summary>
    /// Optional PEM or DER file of the CA that issued the domain controller's certificate. When set, the
    /// certificate must chain to this CA only; when empty, it must chain to a CA the server's trust store trusts.
    /// </summary>
    public string? TrustedCaCertificatePath { get; set; }

    /// <summary>Rejects revoked certificates; when the revocation status cannot be determined the connection fails.</summary>
    public bool CheckCertificateRevocation { get; set; } = true;

    /// <summary>Limit for the TCP connection and the TLS handshake.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>Limit for each bind or search.</summary>
    public int OperationTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Users of the Development-only fake directory (<see cref="DirectoryMode.Fake"/>). Kept in the developer's
    /// user-secrets, never in the repository; refused in any other environment.
    /// </summary>
    public IList<FakeDirectoryUser> FakeUsers { get; } = [];
}

public sealed class FakeDirectoryUser
{
    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    /// <summary>Whether the user counts as a member of the allowed group.</summary>
    public bool IsAllowedGroupMember { get; set; } = true;

    public bool IsDisabled { get; set; }
}

public enum DirectoryMode
{
    /// <summary>The on-premises Active Directory over LDAPS. The only mode allowed outside Development.</summary>
    Ldap = 0,

    /// <summary>
    /// A stand-in directory for local development while the AD details are unknown. The application refuses to
    /// start with it in any environment other than Development.
    /// </summary>
    Fake = 1,
}

public enum NestedGroupPolicy
{
    /// <summary>Only direct members of the allowed group (or users whose primary group it is) get access.</summary>
    DirectMembershipOnly = 1,

    /// <summary>Members of groups nested inside the allowed group, at any depth, also get access.</summary>
    IncludeNested = 2,
}
