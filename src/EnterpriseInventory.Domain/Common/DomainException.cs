namespace EnterpriseInventory.Domain.Common;

/// <summary>
/// A business rule was violated. <see cref="Code"/> is a stable identifier that the API maps to a
/// Turkish message for the user; <see cref="Exception.Message"/> is for logs and developers.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
