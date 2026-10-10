namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// The domain controller could not be used: it is unreachable, too slow, or its certificate was rejected.
/// Sign-in fails closed when this is thrown. The message names the server and the reason, never a credential.
/// </summary>
public sealed class DirectoryUnavailableException : Exception
{
    public DirectoryUnavailableException()
    {
    }

    public DirectoryUnavailableException(string message)
        : base(message)
    {
    }

    public DirectoryUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DirectoryUnavailableException(DirectoryFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public DirectoryFailure Failure { get; }
}

public enum DirectoryFailure
{
    Unknown = 0,
    Unreachable = 1,
    Timeout = 2,
    CertificateRejected = 3,
    TlsHandshakeFailed = 4,
}
