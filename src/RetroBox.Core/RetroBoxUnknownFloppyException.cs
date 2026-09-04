namespace RetroBox.Core;

/// <summary>
/// Thrown when a floppy ID is not present in the catalog. Kept distinct from the base
/// RetroBoxCatalogException so callers (the web endpoints) can route it to 404 by type instead of
/// matching on the exception message, which breaks silently if the message wording ever changes.
/// </summary>
public sealed class RetroBoxUnknownFloppyException(string message) : RetroBoxCatalogException(message);
