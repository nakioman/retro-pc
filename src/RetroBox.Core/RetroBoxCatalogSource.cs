namespace RetroBox.Core;

/// <summary>The catalog and its last load error, read together so a caller never pairs a fresh catalog with a stale banner.</summary>
public sealed record RetroBoxCatalogSnapshot(RetroBoxCatalogData Catalog, string? Error);

/// <summary>A read-through view of the catalog, which can change while the daemon runs.</summary>
public interface IRetroBoxCatalogSource
{
    RetroBoxCatalogSnapshot Snapshot { get; }

    RetroBoxCatalogData Current => Snapshot.Catalog;

    /// <summary>Why the last load or reload was rejected, or null when the catalog is good.</summary>
    string? LastError => Snapshot.Error;

    /// <summary>Reloads now if this source can. Returns true when the catalog was replaced.</summary>
    bool TryReload() => false;
}

public sealed class RetroBoxStaticCatalogSource(RetroBoxCatalogData catalog) : IRetroBoxCatalogSource
{
    public RetroBoxCatalogSnapshot Snapshot => new(catalog, null);
}
