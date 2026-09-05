using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxCatalogEndpoints
{
    public static RetroBoxCatalogView BuildCatalogView(IRetroBoxCatalogSource source)
    {
        var snapshot = source.Snapshot;

        var floppies = snapshot.Catalog.Floppies
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new RetroBoxFloppyView(
                entry.Key,
                entry.Value.Label,
                entry.Value.Mode,
                entry.Value.Size,
                entry.Value.Nfc))
            .ToArray();

        return new RetroBoxCatalogView(floppies, snapshot.Error);
    }
}
