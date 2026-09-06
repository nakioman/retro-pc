using System.Collections.Concurrent;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RetroBox.Core;

public sealed class RetroBoxScraperSettingsStore(string? rootPath = null)
{
    private const string SettingsFileName = "scraper.yaml";

    private static readonly ConcurrentDictionary<string, object> SynchronizationRoots = new(StringComparer.Ordinal);

    private readonly string rootPath = string.IsNullOrWhiteSpace(rootPath)
        ? RetroBoxConfigStore.DefaultRootPath
        : Path.GetFullPath(rootPath);

    private readonly object synchronizationRoot = SynchronizationRoots.GetOrAdd(
        string.IsNullOrWhiteSpace(rootPath) ? RetroBoxConfigStore.DefaultRootPath : Path.GetFullPath(rootPath),
        _ => new object());

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
        lock (synchronizationRoot)
        {
            return LoadUnderLock();
        }
    }

    public void Save(RetroBoxScraperSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (synchronizationRoot)
        {
            SaveUnderLock(settings);
        }
    }

    public RetroBoxScraperSettings Update(Action<RetroBoxScraperSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (synchronizationRoot)
        {
            var settings = LoadUnderLock();
            update(settings);
            SaveUnderLock(settings);
            return settings;
        }
    }

    private RetroBoxScraperSettings LoadUnderLock()
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

    private void SaveUnderLock(RetroBoxScraperSettings settings)
    {
        Directory.CreateDirectory(rootPath);
        var path = ResolvePath();
        var stagedPath = Path.Combine(rootPath, $".{SettingsFileName}-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = CreateStagingFile(stagedPath))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(serializer.Serialize(settings));
            }

            File.Move(stagedPath, path, overwrite: true);
            SetOwnerOnlyPermissions(path);
        }
        finally
        {
            try
            {
                File.Delete(stagedPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private string ResolvePath() => Path.Combine(rootPath, SettingsFileName);

    private static FileStream CreateStagingFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return File.Create(path);
        }

        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
    }

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
