namespace RetroBox.Core;

public sealed record RetroBoxCatalogData(
    RetroBoxConfig Config,
    IReadOnlyDictionary<string, RetroBoxVm> Vms,
    IReadOnlyDictionary<string, RetroBoxFloppy> Floppies)
{
    public IReadOnlyDictionary<string, RetroBoxGame> Games { get; init; } =
        new Dictionary<string, RetroBoxGame>(StringComparer.Ordinal);

    public static RetroBoxCatalogData Empty { get; } = new(
        new RetroBoxConfig(),
        new Dictionary<string, RetroBoxVm>(StringComparer.Ordinal),
        new Dictionary<string, RetroBoxFloppy>(StringComparer.Ordinal));
}

public sealed record RetroBoxConfig
{
    public string DefaultVm { get; set; } = string.Empty;

    public string? FloppyControlSocketPath { get; set; }

    public string? SerialPort { get; set; }

    public int? SerialBaud { get; set; }
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

    public string? NfcUid { get; set; }
}

public sealed record RetroBoxGame
{
    public string Label { get; set; } = string.Empty;

    public string? Cover { get; set; }

    public int? ScreenScraperId { get; set; }

    public List<string> FloppyIds { get; set; } = [];
}

internal sealed record RetroBoxGameCatalog
{
    public Dictionary<string, RetroBoxGame> Games { get; set; } = new(StringComparer.Ordinal);
}

internal sealed record RetroBoxVmCatalog
{
    public Dictionary<string, RetroBoxVm> Vms { get; set; } = new(StringComparer.Ordinal);
}

internal sealed record RetroBoxFloppyCatalog
{
    public Dictionary<string, RetroBoxFloppy> Floppies { get; set; } = new(StringComparer.Ordinal);
}
