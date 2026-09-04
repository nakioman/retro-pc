namespace RetroBox.Core;

/// <summary>Catalog mutations that keep the YAML loadable at every step.</summary>
public sealed class RetroBoxFloppyLibrary(RetroBoxConfigStore store, Action<string>? deleteFile = null)
{
    private readonly Action<string> deleteImageFile = deleteFile ?? File.Delete;
    private readonly Lock gate = new();

    /// <summary>
    /// Removes a floppy. The catalog entry goes first and the image file last: a failed delete
    /// leaves an orphaned file, which is untidy, while the reverse order leaves
    /// RetroBoxConfigStore.Validate throwing on a missing image — and that stops both the daemon
    /// and `retrobox boot`.
    /// </summary>
    public void Delete(string id)
    {
        lock (gate)
        {
            var data = LoadOrThrow();
            var floppy = RequireFloppy(data, id);

            var floppies = new Dictionary<string, RetroBoxFloppy>(data.Floppies, StringComparer.Ordinal);
            floppies.Remove(id);
            store.Save(data with { Floppies = floppies });

            try
            {
                deleteImageFile(floppy.Image);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new RetroBoxCatalogException(
                    $"Floppy '{id}' was removed from the catalog, but its image '{floppy.Image}' could not be deleted: {ex.Message}",
                    ex);
            }
        }
    }

    /// <summary>
    /// Updates the label and/or the mode. Changing the mode invalidates any written tag, because
    /// the tag payload is `<id>,<mode>`: a floppy written as `ro` and switched to `rw` would keep
    /// mounting read-only with no visible cause.
    /// </summary>
    public void UpdateLabelAndMode(string id, string? label, string? mode)
    {
        lock (gate)
        {
            var data = LoadOrThrow();
            var floppy = RequireFloppy(data, id);

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

    /// <summary>
    /// Runs an arbitrary catalog-mutating action under the same instance lock as Delete and
    /// UpdateLabelAndMode. The upload endpoint does its own load/resolve-id/save sequence outside
    /// this class (through RetroBoxFloppyImporter); without sharing this lock, two concurrent
    /// requests could resolve the same free ID from the same stale snapshot and clobber each
    /// other's save — a double-tapped upload button on a phone is a realistic way to hit this.
    /// </summary>
    public void RunExclusively(Action action)
    {
        lock (gate)
        {
            action();
        }
    }

    private RetroBoxCatalogData LoadOrThrow()
    {
        try
        {
            return store.Load();
        }
        catch (RetroBoxCatalogException ex)
        {
            throw new RetroBoxCatalogUnavailableException($"The catalog could not be loaded: {ex.Message}", ex);
        }
    }

    private static RetroBoxFloppy RequireFloppy(RetroBoxCatalogData data, string id)
    {
        if (!data.Floppies.TryGetValue(id, out var floppy))
        {
            throw new RetroBoxUnknownFloppyException($"Unknown floppy '{id}'.");
        }

        return floppy;
    }
}
