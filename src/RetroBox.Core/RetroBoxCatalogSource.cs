namespace RetroBox.Core;

/// <summary>A read-through view of the catalog, which can change while the daemon runs.</summary>
public interface IRetroBoxCatalogSource
{
    RetroBoxCatalogData Current { get; }

    /// <summary>Why the last load or reload was rejected, or null when the catalog is good.</summary>
    string? LastError { get; }

    /// <summary>Reloads now if this source can. Returns true when the catalog was replaced.</summary>
    bool TryReload() => false;
}

public sealed class RetroBoxStaticCatalogSource(RetroBoxCatalogData catalog) : IRetroBoxCatalogSource
{
    public RetroBoxCatalogData Current => catalog;

    public string? LastError => null;
}
