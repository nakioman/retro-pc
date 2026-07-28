namespace RetroBox.Core;

internal static class RetroBoxCatalogValidation
{
    public static void RequireCatalogId(this string value, string name)
    {
        value.RequireCatalogValue(name);
        if (!IsCatalogId(value))
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

    private static bool IsCatalogId(string value)
    {
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
}
