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

    public RetroBoxGame CreateGame(string id, string label)
    {
        lock (gate)
        {
            var data = LoadOrThrow();
            if (data.Games.ContainsKey(id))
            {
                throw new RetroBoxCatalogException($"Game '{id}' already exists.");
            }

            var game = new RetroBoxGame { Label = label };
            var games = new Dictionary<string, RetroBoxGame>(data.Games, StringComparer.Ordinal)
            {
                [id] = game,
            };

            store.Save(data with { Games = games });
            return game;
        }
    }

    public RetroBoxGame? UpdateGame(string id, string? label, IReadOnlyList<string> floppyIds)
    {
        lock (gate)
        {
            var data = LoadOrThrow();
            if (!data.Games.ContainsKey(id))
            {
                return null;
            }

            foreach (var floppyId in floppyIds)
            {
                RequireFloppy(data, floppyId);
            }

            var games = new Dictionary<string, RetroBoxGame>(data.Games, StringComparer.Ordinal);
            var game = new RetroBoxGame { Label = label ?? data.Games[id].Label, FloppyIds = [.. floppyIds] };
            games[id] = game;
            store.Save(data with { Games = games });
            return game;
        }
    }

    public bool DeleteGame(string id)
    {
        lock (gate)
        {
            var data = LoadOrThrow();
            var games = new Dictionary<string, RetroBoxGame>(data.Games, StringComparer.Ordinal);
            if (!games.Remove(id))
            {
                return false;
            }

            store.Save(data with { Games = games });
            return true;
        }
    }

    /// <summary>
    /// Records a written tag. Any other floppy holding this UID loses it: the tag is a physical
    /// object, so once it is reassigned the old entry genuinely has no tag, and leaving it
    /// claiming one would let the mount guard accept a stale tag.
    /// </summary>
    /// <param name="expectedMode">
    /// The mode that was actually written onto the tag's payload, re-checked here against the
    /// current catalog entry. A concurrent UpdateLabelAndMode call between the write and this
    /// commit changes the mode and deliberately clears Nfc/NfcUid for exactly this reason — the
    /// tag's payload is `&lt;id&gt;,&lt;mode&gt;`, so committing this assignment over a since-changed
    /// mode would silently resurrect a tag whose payload the catalog no longer matches. Null skips
    /// the check for callers that have not written a mode-bearing payload.
    /// </param>
    public void AssignTag(string id, string tagUid, string? expectedMode = null)
    {
        RunExclusively(() =>
        {
            var data = LoadOrThrow();
            var floppy = RequireFloppy(data, id);

            if (expectedMode is not null && !string.Equals(floppy.Mode, expectedMode, StringComparison.Ordinal))
            {
                throw new RetroBoxFloppyModeChangedException(
                    $"Floppy '{id}' mode changed to '{floppy.Mode}' before the tag write could be recorded.");
            }

            var floppies = new Dictionary<string, RetroBoxFloppy>(data.Floppies, StringComparer.Ordinal);

            foreach (var (otherId, other) in data.Floppies)
            {
                if (!string.Equals(otherId, id, StringComparison.Ordinal)
                    && string.Equals(other.NfcUid, tagUid, StringComparison.Ordinal))
                {
                    var released = other with { };
                    released.Nfc = false;
                    released.NfcUid = null;
                    floppies[otherId] = released;
                }
            }

            var assigned = floppy with { };
            assigned.Nfc = true;
            assigned.NfcUid = tagUid;
            floppies[id] = assigned;

            store.Save(data with { Floppies = floppies });
        });
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

    /// <summary>
    /// Confirms the catalog can currently be loaded and validated, without mutating anything.
    /// The upload endpoint calls this before handing off to RetroBoxFloppyImporter, whose own
    /// store.Load()/store.Save() would otherwise surface an unrelated broken catalog entry as if
    /// the uploaded file itself were invalid. Safe to call from inside RunExclusively: the
    /// underlying Lock is reentrant on the same thread.
    /// </summary>
    public void EnsureCatalogIsLoadable()
    {
        lock (gate)
        {
            LoadOrThrow();
        }
    }

    private RetroBoxCatalogData LoadOrThrow()
    {
        try
        {
            return store.Load();
        }
        catch (Exception ex) when (ex is RetroBoxCatalogException or IOException or UnauthorizedAccessException)
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
