namespace RetroBox.Core;

public sealed record RetroBoxCatalogData(
    RetroBoxConfig Config,
    IReadOnlyDictionary<string, RetroBoxVm> Vms,
    IReadOnlyDictionary<string, RetroBoxFloppy> Floppies);

public sealed record RetroBoxConfig
{
    public string DefaultVm { get; set; } = string.Empty;

    public string? FloppyControlSocketPath { get; set; }
}

public sealed record RetroBoxVm
{
    public string Label { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}

public sealed record RetroBoxFloppy
{
    public string Label { get; set; } = string.Empty;

    public string Image { get; set; } = string.Empty;

    public string Mode { get; set; } = RetroBoxFloppyCatalogRules.ReadOnlyMode;

    public string Size { get; set; } = RetroBoxFloppyCatalogRules.DefaultImportSize;

    public bool Nfc { get; set; }
}

public sealed record RetroBoxGame
{
    public string Label { get; init; } = string.Empty;

    public string? DefaultVm { get; init; }

    public List<string> FloppyIds { get; init; } = [];
}

internal sealed record RetroBoxVmCatalog
{
    public Dictionary<string, RetroBoxVm> Vms { get; set; } = new(StringComparer.Ordinal);
}

internal sealed record RetroBoxFloppyCatalog
{
    public Dictionary<string, RetroBoxFloppy> Floppies { get; set; } = new(StringComparer.Ordinal);
}
