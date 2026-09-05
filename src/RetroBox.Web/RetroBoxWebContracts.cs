using System.Text.Json.Serialization;

namespace RetroBox.Web;

public sealed record RetroBoxFloppyView(string Id, string Label, string Mode, string Size, bool Nfc);

public sealed record RetroBoxCatalogView(RetroBoxFloppyView[] Floppies, string? CatalogError);

public sealed record RetroBoxErrorView(string Code, string Message);

public sealed record RetroBoxFloppyPatch(string? Label, string? Mode);

public sealed record RetroBoxDriveView(string State, string? FloppyId, string? Mode, string? TagUid);

public sealed record RetroBoxNfcWriteRequest(string FloppyId, bool Confirm);

public sealed record RetroBoxNfcWriteResult(string Code, string? PreviousFloppyId, string? Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RetroBoxCatalogView))]
[JsonSerializable(typeof(RetroBoxErrorView))]
[JsonSerializable(typeof(RetroBoxFloppyPatch))]
[JsonSerializable(typeof(RetroBoxDriveView))]
[JsonSerializable(typeof(RetroBoxNfcWriteRequest))]
[JsonSerializable(typeof(RetroBoxNfcWriteResult))]
public sealed partial class RetroBoxWebJsonContext : JsonSerializerContext;
