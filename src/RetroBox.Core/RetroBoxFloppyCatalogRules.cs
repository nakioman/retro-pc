namespace RetroBox.Core;

public static class RetroBoxFloppyCatalogRules
{
    public const string ReadOnlyMode = "ro";
    public const string ReadWriteMode = "rw";
    public const string Size360K = "360K";
    public const string Size720K = "720K";
    public const string Size12M = "1.2M";
    public const string Size144M = "1.44M";
    public const string DefaultImportSize = Size144M;

    public static readonly string[] ValidModes =
    [
        ReadOnlyMode,
        ReadWriteMode,
    ];

    private static readonly HashSet<string> ValidModeSet = new(ValidModes, StringComparer.Ordinal);

    public static readonly string[] ValidSizes =
    [
        Size360K,
        Size720K,
        Size12M,
        Size144M,
    ];

    private static readonly HashSet<string> ValidSizeSet = new(ValidSizes, StringComparer.Ordinal);

    public static bool IsValidMode(string mode)
    {
        return ValidModeSet.Contains(mode);
    }

    public static bool IsValidSize(string size)
    {
        return ValidSizeSet.Contains(size);
    }

}
