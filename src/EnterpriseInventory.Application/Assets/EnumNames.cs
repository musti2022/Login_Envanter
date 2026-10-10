namespace EnterpriseInventory.Application.Assets;

/// <summary>Enum values as the API exchanges them: by name, ignoring case. Numbers are not accepted.</summary>
internal static class EnumNames
{
    public static bool IsDefined<T>(string? name)
        where T : struct, Enum =>
        TryParse<T>(name, out _);

    public static T Parse<T>(string? name)
        where T : struct, Enum =>
        TryParse<T>(name, out var value) ? value : throw new ArgumentException($"'{name}' is not a {typeof(T).Name}.", nameof(name));

    public static bool TryParse<T>(string? name, out T value)
        where T : struct, Enum
    {
        value = default;

        // Enum.TryParse also accepts "2", "-1" and "Laptop, Desktop"; only a single defined name is a valid value.
        return !string.IsNullOrWhiteSpace(name)
            && name.All(char.IsAsciiLetter)
            && Enum.TryParse(name, ignoreCase: true, out value)
            && Enum.IsDefined(value);
    }
}
