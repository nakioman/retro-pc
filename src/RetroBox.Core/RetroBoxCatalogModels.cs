namespace RetroBox.Core;

public sealed record RetroBoxCatalogData(
    RetroBoxConfig Config,
    IReadOnlyDictionary<string, RetroBoxVm> Vms,
    IReadOnlyDictionary<string, RetroBoxFloppy> Floppies,
    IReadOnlyDictionary<string, RetroBoxGame> Games);

public sealed record RetroBoxConfig
{
    public string DefaultVm { get; init; } = string.Empty;
}

public sealed record RetroBoxVm
{
    public string Label { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;
}

public sealed record RetroBoxFloppy
{
    public string Label { get; init; } = string.Empty;

    public string Image { get; init; } = string.Empty;

    public string Mode { get; init; } = RetroBoxFloppyCatalogRules.ReadOnlyMode;

    public string Size { get; init; } = RetroBoxFloppyCatalogRules.DefaultImportSize;
}

public sealed record RetroBoxGame
{
    public string Label { get; init; } = string.Empty;

    public string? DefaultVm { get; init; }

    public List<string> FloppyIds { get; init; } = [];
}

internal sealed record RetroBoxVmCatalog
{
    public Dictionary<string, RetroBoxVm> Vms { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record RetroBoxFloppyCatalog
{
    public Dictionary<string, RetroBoxFloppy> Floppies { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record RetroBoxGameCatalog
{
    public Dictionary<string, RetroBoxGame> Games { get; init; } = new(StringComparer.Ordinal);
}
