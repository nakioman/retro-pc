namespace RetroBox.Core;

/// <summary>Catalog mutations that keep the YAML loadable at every step.</summary>
public sealed class RetroBoxFloppyLibrary(RetroBoxConfigStore store)
{
    /// <summary>
    /// Removes a floppy. The catalog entry goes first and the image file last: a failed delete
    /// leaves an orphaned file, which is untidy, while the reverse order leaves
    /// RetroBoxConfigStore.Validate throwing on a missing image — and that stops both the daemon
    /// and `retrobox boot`.
    /// </summary>
    public void Delete(string id)
    {
        var data = store.Load();
        if (!data.Floppies.TryGetValue(id, out var floppy))
        {
            throw new RetroBoxCatalogException($"Unknown floppy '{id}'.");
        }

        var floppies = new Dictionary<string, RetroBoxFloppy>(data.Floppies, StringComparer.Ordinal);
        floppies.Remove(id);
        store.Save(data with { Floppies = floppies });

        try
        {
            File.Delete(floppy.Image);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RetroBoxCatalogException(
                $"Floppy '{id}' was removed from the catalog, but its image '{floppy.Image}' could not be deleted: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Updates the label and/or the mode. Changing the mode invalidates any written tag, because
    /// the tag payload is `<id>,<mode>`: a floppy written as `ro` and switched to `rw` would keep
    /// mounting read-only with no visible cause.
    /// </summary>
    public void UpdateLabelAndMode(string id, string? label, string? mode)
    {
        var data = store.Load();
        if (!data.Floppies.TryGetValue(id, out var floppy))
        {
            throw new RetroBoxCatalogException($"Unknown floppy '{id}'.");
        }

        if (mode is not null && !RetroBoxFloppyCatalogRules.IsValidMode(mode))
        {
            throw new RetroBoxCatalogException($"Invalid floppy mode '{mode}' for floppy '{id}'.");
        }

        var updated = floppy with { };
        if (!string.IsNullOrWhiteSpace(label))
        {
            updated.Label = label;
        }

        if (mode is not null && mode != floppy.Mode)
        {
            updated.Mode = mode;
            updated.Nfc = false;
            updated.NfcUid = null;
        }

        var floppies = new Dictionary<string, RetroBoxFloppy>(data.Floppies, StringComparer.Ordinal)
        {
            [id] = updated,
        };

        store.Save(data with { Floppies = floppies });
    }
}
