namespace EnterpriseInventory.Domain.Common;

internal static class Guard
{
    /// <summary>Returns the trimmed value, or throws when it is empty or longer than <paramref name="maxLength"/>.</summary>
    public static string Required(string? value, int maxLength, string paramName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException(DomainErrors.Required, $"{paramName} is required.");
        }

        return MaxLength(trimmed, maxLength, paramName);
    }

    /// <summary>Returns the trimmed value, <c>null</c> for empty input, or throws when it is too long.</summary>
    public static string? Optional(string? value, int maxLength, string paramName)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : MaxLength(trimmed, maxLength, paramName);
    }

    public static T Defined<T>(T value, string paramName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new DomainException(DomainErrors.InvalidValue, $"{paramName} has an undefined value '{value}'.");
        }

        return value;
    }

    public static Guid NotEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException(DomainErrors.Required, $"{paramName} is required.");
        }

        return value;
    }

    private static string MaxLength(string value, int maxLength, string paramName)
    {
        if (value.Length > maxLength)
        {
            throw new DomainException(DomainErrors.TooLong, $"{paramName} must be at most {maxLength} characters.");
        }

        return value;
    }
}
