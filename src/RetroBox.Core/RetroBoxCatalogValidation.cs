namespace RetroBox.Core;

internal static class RetroBoxCatalogValidation
{
    public static void RequireCatalogId(this string value, string name)
    {
        value.RequireCatalogValue(name);
        if (value.Contains(' ', StringComparison.Ordinal))
        {
            throw new RetroBoxCatalogException($"{name} '{value}' must not contain spaces.");
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
