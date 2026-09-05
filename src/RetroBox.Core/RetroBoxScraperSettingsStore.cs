using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RetroBox.Core;

public sealed class RetroBoxScraperSettingsStore(string? rootPath = null)
{
    private const string SettingsFileName = "scraper.yaml";

    private readonly string rootPath = string.IsNullOrWhiteSpace(rootPath)
        ? RetroBoxConfigStore.DefaultRootPath
        : Path.GetFullPath(rootPath);

    private readonly IDeserializer deserializer = new StaticDeserializerBuilder(new RetroBoxYamlContext())
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithDuplicateKeyChecking()
        .Build();

    private readonly ISerializer serializer = new StaticSerializerBuilder(new RetroBoxYamlContext())
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public RetroBoxScraperSettings Load()
    {
        var path = ResolvePath();
        if (!File.Exists(path))
        {
            return new RetroBoxScraperSettings();
        }

        try
        {
            return deserializer.Deserialize<RetroBoxScraperSettings>(File.ReadAllText(path))
                ?? throw new RetroBoxCatalogException($"YAML file '{path}' is empty.");
        }
        catch (YamlException ex)
        {
            throw new RetroBoxCatalogException($"YAML file '{path}' is invalid: {ex.Message}", ex);
        }
    }

    public void Save(RetroBoxScraperSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(rootPath);
        var path = ResolvePath();
        File.WriteAllText(path, serializer.Serialize(settings));
        SetOwnerOnlyPermissions(path);
    }

    private string ResolvePath() => Path.Combine(rootPath, SettingsFileName);

    private static void SetOwnerOnlyPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}
