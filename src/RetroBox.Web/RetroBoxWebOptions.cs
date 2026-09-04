using RetroBox.Core;

namespace RetroBox.Web;

public sealed record RetroBoxWebOptions
{
    public const int DefaultPort = 8080;

    public int Port { get; init; } = DefaultPort;

    public string ConfigRoot { get; init; } = RetroBoxConfigStore.DefaultRootPath;

    public string ScratchRoot { get; init; } = RetroBoxFloppyImporter.DefaultScratchRoot;

    public string CatalogedRoot { get; init; } = RetroBoxFloppyImporter.DefaultCatalogedRoot;
}
