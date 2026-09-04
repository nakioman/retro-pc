using System.Text.Json.Serialization;

namespace RetroBox.Web;

public sealed record RetroBoxFloppyView(string Id, string Label, string Mode, string Size, bool Nfc);

public sealed record RetroBoxCatalogView(RetroBoxFloppyView[] Floppies, string? CatalogError);

public sealed record RetroBoxErrorView(string Code, string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RetroBoxCatalogView))]
[JsonSerializable(typeof(RetroBoxErrorView))]
public sealed partial class RetroBoxWebJsonContext : JsonSerializerContext;
