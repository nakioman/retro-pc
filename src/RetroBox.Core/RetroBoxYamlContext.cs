using YamlDotNet.Serialization;

namespace RetroBox.Core;

[YamlStaticContext]
[YamlSerializable(typeof(RetroBoxConfig))]
[YamlSerializable(typeof(RetroBoxVm))]
[YamlSerializable(typeof(RetroBoxFloppy))]
[YamlSerializable(typeof(RetroBoxVmCatalog))]
[YamlSerializable(typeof(RetroBoxFloppyCatalog))]
[YamlSerializable(typeof(RetroBoxGame))]
[YamlSerializable(typeof(RetroBoxGameCatalog))]
public partial class RetroBoxYamlContext : StaticContext;
