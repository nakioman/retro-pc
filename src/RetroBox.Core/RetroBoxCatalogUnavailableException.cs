namespace RetroBox.Core;

/// <summary>
/// Thrown when the catalog itself could not be loaded or validated — for example another
/// floppy's image went missing out from under it. Kept distinct from
/// RetroBoxUnknownFloppyException and from a plain RetroBoxCatalogException so a broken catalog
/// is never mistaken for "this specific floppy doesn't exist" or "this specific request was bad."
/// </summary>
public sealed class RetroBoxCatalogUnavailableException(string message, Exception innerException)
    : RetroBoxCatalogException(message, innerException);
