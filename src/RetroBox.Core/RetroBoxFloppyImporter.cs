namespace RetroBox.Core;

public sealed record RetroBoxFloppyImportRequest
{
    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string ImagePath { get; init; } = string.Empty;

    public string Mode { get; init; } = RetroBoxFloppyCatalogRules.ReadOnlyMode;

    public string Size { get; init; } = RetroBoxFloppyCatalogRules.DefaultImportSize;

    public string ConfigRoot { get; init; } = RetroBoxConfigStore.DefaultRootPath;

    public string ScratchRoot { get; init; } = RetroBoxFloppyImporter.DefaultScratchRoot;

    public string CatalogedRoot { get; init; } = RetroBoxFloppyImporter.DefaultCatalogedRoot;
}

public sealed record RetroBoxFloppyImportResult(string Id, string ImagePath);

public sealed class RetroBoxFloppyImporter
{
    public const string DefaultScratchRoot = "/data/floppies/scratch";
    public const string DefaultCatalogedRoot = "/data/floppies/cataloged";

    public RetroBoxFloppyImportResult Import(RetroBoxFloppyImportRequest request)
    {
        request.Id.RequireCatalogId("floppy ID");
        request.Label.RequireCatalogValue($"Floppy '{request.Id}' label");
        request.ImagePath.RequireCatalogValue($"Floppy '{request.Id}' image");

        if (!RetroBoxFloppyCatalogRules.IsValidMode(request.Mode))
        {
            throw new RetroBoxCatalogException($"Invalid floppy mode '{request.Mode}' for floppy '{request.Id}'.");
        }

        RequireSize(request.Size, request.Id);
        var sourcePath = Path.GetFullPath(request.ImagePath);
        var scratchRoot = Path.GetFullPath(request.ScratchRoot);
        var catalogedRoot = Path.GetFullPath(request.CatalogedRoot);

        if (!IsPathWithinDirectory(sourcePath, scratchRoot))
        {
            throw new RetroBoxCatalogException(
                $"Floppy image '{sourcePath}' must be under scratch root '{scratchRoot}'.");
        }

        var targetPath = Path.Combine(catalogedRoot, Path.GetFileName(sourcePath));

        var store = new RetroBoxConfigStore(request.ConfigRoot);
        var data = store.Load();
        if (data.Floppies.ContainsKey(request.Id))
        {
            throw new RetroBoxCatalogException($"Floppy ID '{request.Id}' already exists.");
        }

        Directory.CreateDirectory(catalogedRoot);
        MoveImage(sourcePath, targetPath);

        try
        {
            var floppies = new Dictionary<string, RetroBoxFloppy>(data.Floppies, StringComparer.Ordinal)
            {
                [request.Id] = new()
                {
                    Label = request.Label,
                    Image = targetPath,
                    Mode = request.Mode,
                    Size = request.Size,
                },
            };

            store.Save(data with { Floppies = floppies });
        }
        catch
        {
            RestoreMovedImage(sourcePath, targetPath);
            throw;
        }

        return new RetroBoxFloppyImportResult(request.Id, targetPath);
    }

    private static void RequireSize(string size, string id)
    {
        if (RetroBoxFloppyCatalogRules.IsValidSize(size))
        {
            return;
        }

        throw new RetroBoxCatalogException($"Invalid floppy size '{size}' for floppy '{id}'.");
    }

    private static bool IsPathWithinDirectory(string path, string directory)
    {
        var normalizedDirectory = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedDirectory, StringComparison.Ordinal);
    }

    private static void MoveImage(string sourcePath, string targetPath)
    {
        try
        {
            File.Move(sourcePath, targetPath, overwrite: false);
        }
        catch (FileNotFoundException)
        {
            throw new RetroBoxCatalogException($"Floppy image '{sourcePath}' does not exist.");
        }
        catch (IOException ex) when (File.Exists(targetPath))
        {
            throw new RetroBoxCatalogException($"Cataloged floppy image '{targetPath}' already exists.", ex);
        }
    }

    private static void RestoreMovedImage(string sourcePath, string targetPath)
    {
        if (File.Exists(sourcePath) || !File.Exists(targetPath))
        {
            return;
        }

        File.Move(targetPath, sourcePath, overwrite: false);
    }
}
