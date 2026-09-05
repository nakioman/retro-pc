using System.Text.Json.Serialization;

namespace RetroBox.Web;

public sealed record RetroBoxFloppyView(string Id, string Label, string Mode, string Size, bool Nfc);

public sealed record RetroBoxCatalogView(
    RetroBoxFloppyView[] Floppies,
    RetroBoxGameView[] Games,
    RetroBoxFloppyView[] UngroupedFloppies,
    string? CatalogError);

public sealed record RetroBoxGameView(string Id, string Label, RetroBoxFloppyView[] Floppies);

public sealed record RetroBoxErrorView(string Code, string Message);

public sealed record RetroBoxFloppyPatch(string? Label, string? Mode);

public sealed record RetroBoxGameCreate(string? Id, string? Label);

public sealed record RetroBoxGamePatch(string? Label, string[]? FloppyIds);

public sealed record RetroBoxDriveView(string State, string? FloppyId, string? Mode, string? TagUid);

/// <param name="TagUid">
/// The tag the caller believes is seated, echoed back from a tag-already-assigned 409. Required
/// on a confirmed request; optional otherwise.
/// </param>
public sealed record RetroBoxNfcWriteRequest(string FloppyId, bool Confirm, string? TagUid);

public sealed record RetroBoxNfcWriteResult(string Code, string? PreviousFloppyId, string? Message, string? TagUid);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RetroBoxCatalogView))]
[JsonSerializable(typeof(RetroBoxGameView))]
[JsonSerializable(typeof(RetroBoxErrorView))]
[JsonSerializable(typeof(RetroBoxFloppyPatch))]
[JsonSerializable(typeof(RetroBoxGameCreate))]
[JsonSerializable(typeof(RetroBoxGamePatch))]
[JsonSerializable(typeof(RetroBoxDriveView))]
[JsonSerializable(typeof(RetroBoxNfcWriteRequest))]
[JsonSerializable(typeof(RetroBoxNfcWriteResult))]
public sealed partial class RetroBoxWebJsonContext : JsonSerializerContext;
