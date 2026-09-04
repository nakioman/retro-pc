namespace RetroBox.Core;

/// <summary>
/// Thrown by AssignTag when the mode it was given no longer matches the catalog's current entry.
/// Kept distinct from a plain RetroBoxCatalogException so callers can route this one specific,
/// recoverable condition (a concurrent mode change) to its own response without also catching —
/// and mislabelling — an unrelated validation failure that happens to also be a
/// RetroBoxCatalogException.
/// </summary>
public sealed class RetroBoxFloppyModeChangedException(string message) : RetroBoxCatalogException(message);
