using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxCatalogEndpoints
{
    public static RetroBoxCatalogView BuildCatalogView(IRetroBoxCatalogSource source)
    {
        var snapshot = source.Snapshot;

        var floppies = snapshot.Catalog.Floppies
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Key, entry => ToFloppyView(entry.Key, entry.Value), StringComparer.Ordinal);

        var groupedFloppyIds = new HashSet<string>(
            snapshot.Catalog.Games.Values.SelectMany(game => game.FloppyIds),
            StringComparer.Ordinal);
        var games = snapshot.Catalog.Games
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new RetroBoxGameView(
                entry.Key,
                entry.Value.Label,
                entry.Value.FloppyIds.Select(id => floppies[id]).ToArray()))
            .ToArray();
        var ungroupedFloppies = floppies
            .Where(entry => !groupedFloppyIds.Contains(entry.Key))
            .Select(entry => entry.Value)
            .ToArray();

        return new RetroBoxCatalogView(
            floppies.Values.ToArray(),
            games,
            ungroupedFloppies,
            snapshot.Error);
    }

    private static RetroBoxFloppyView ToFloppyView(string id, RetroBoxFloppy floppy)
    {
        return new RetroBoxFloppyView(id, floppy.Label, floppy.Mode, floppy.Size, floppy.Nfc);
    }
}
