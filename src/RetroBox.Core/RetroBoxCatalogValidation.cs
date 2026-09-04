namespace RetroBox.Core;

public static class RetroBoxCatalogRules
{
    public static bool IsValidId(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(value[0]) && !char.IsAsciiDigit(value[0]))
        {
            return false;
        }

        var previousWasDash = false;
        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '-')
            {
                if (previousWasDash)
                {
                    return false;
                }

                previousWasDash = true;
                continue;
            }

            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character))
            {
                return false;
            }

            previousWasDash = false;
        }

        return !previousWasDash;
    }

    /// <summary>Reduces a filename to something <see cref="IsValidId"/> accepts, or an empty string.</summary>
    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        var previousWasDash = false;

        foreach (var character in value)
        {
            var lowered = char.ToLowerInvariant(character);
            if (char.IsAsciiLetterLower(lowered) || char.IsAsciiDigit(lowered))
            {
                builder.Append(lowered);
                previousWasDash = false;
                continue;
            }

            if (builder.Length > 0 && !previousWasDash)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}

internal static class RetroBoxCatalogValidation
{
    public static void RequireCatalogId(this string value, string name)
    {
        value.RequireCatalogValue(name);
        if (!RetroBoxCatalogRules.IsValidId(value))
        {
            throw new RetroBoxCatalogException(
                $"{name} '{value}' must contain only lowercase ASCII letters, digits, and single hyphens, and must start and end with a letter or digit.");
        }
    }

    public static void RequireCatalogValue(this string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RetroBoxCatalogException($"{name} is required.");
        }
    }
}
