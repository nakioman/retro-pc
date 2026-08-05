using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RetroBox.Core;

public sealed class RetroBoxConfigStore(string? rootPath = null)
{
    public const string DefaultRootPath = "/data/retrobox";

    private readonly string rootPath = string.IsNullOrWhiteSpace(rootPath)
            ? DefaultRootPath
            : Path.GetFullPath(rootPath);

    private readonly IDeserializer deserializer = new StaticDeserializerBuilder(new RetroBoxYamlContext())
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDuplicateKeyChecking()
            .Build();

    private readonly ISerializer serializer = new StaticSerializerBuilder(new RetroBoxYamlContext())
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

    public RetroBoxCatalogData Load()
    {
        var config = LoadConfig();
        var vms = LoadYaml<RetroBoxVmCatalog>("vms.yaml").Vms;
        var floppies = LoadFloppies();

        var data = new RetroBoxCatalogData(config, vms, floppies);
        Validate(data);
        return data;
    }

    public void Save(RetroBoxCatalogData data)
    {
        Validate(data);
        Directory.CreateDirectory(rootPath);

        SaveYamlSet([
            ("config.yaml", serializer.Serialize(data.Config)),
            ("vms.yaml", serializer.Serialize(new RetroBoxVmCatalog { Vms = new Dictionary<string, RetroBoxVm>(data.Vms, StringComparer.Ordinal) })),
            ("floppies.yaml", serializer.Serialize(new RetroBoxFloppyCatalog { Floppies = new Dictionary<string, RetroBoxFloppy>(data.Floppies, StringComparer.Ordinal) })),
        ]);
    }

    public void UpdateDefaultVm(string vmId)
    {
        var config = LoadConfig();
        SaveYaml("config.yaml", serializer.Serialize(config with { DefaultVm = vmId }));
    }

    private RetroBoxConfig LoadConfig()
    {
        return File.Exists(ResolvePath("config.yaml"))
            ? LoadYaml<RetroBoxConfig>("config.yaml")
            : new RetroBoxConfig();
    }

    private Dictionary<string, RetroBoxFloppy> LoadFloppies()
    {
        return File.Exists(ResolvePath("floppies.yaml"))
            ? LoadYaml<RetroBoxFloppyCatalog>("floppies.yaml").Floppies
            : new Dictionary<string, RetroBoxFloppy>(StringComparer.Ordinal);
    }

    private T LoadYaml<T>(string fileName)
    {
        var path = ResolvePath(fileName);
        if (!File.Exists(path))
        {
            throw new RetroBoxCatalogException($"Required YAML file '{path}' does not exist.");
        }

        try
        {
            var yaml = File.ReadAllText(path);
            return deserializer.Deserialize<T>(yaml)
                ?? throw new RetroBoxCatalogException($"YAML file '{path}' is empty.");
        }
        catch (YamlException ex)
        {
            throw new RetroBoxCatalogException($"YAML file '{path}' is invalid: {ex.Message}", ex);
        }
    }

    private void SaveYamlSet((string FileName, string Yaml)[] files)
    {
        var backups = new List<(string OriginalPath, string BackupPath)>();

        try
        {
            foreach (var (fileName, yaml) in files)
            {
                var path = ResolvePath(fileName);
                if (File.Exists(path))
                {
                    var backupPath = CreateBackupPath(path);
                    File.Copy(path, backupPath, overwrite: false);
                    backups.Add((path, backupPath));
                }

                File.WriteAllText(path, yaml);
            }
        }
        catch
        {
            RestoreBackups(backups);
            throw;
        }
    }

    private void SaveYaml(string fileName, string yaml)
    {
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(ResolvePath(fileName), yaml);
    }

    private string ResolvePath(string fileName)
    {
        return Path.Combine(rootPath, fileName);
    }

    private static string CreateBackupPath(string path)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff");
        return $"{path}.{timestamp}.bak";
    }

    private static void RestoreBackups(IEnumerable<(string OriginalPath, string BackupPath)> backups)
    {
        foreach (var (originalPath, backupPath) in backups.Reverse())
        {
            try
            {
                File.Copy(backupPath, originalPath, overwrite: true);
            }
            catch
            {
                // Continue restoring any other catalog files that may have already been replaced.
            }
        }
    }

    private static void Validate(RetroBoxCatalogData data)
    {
        if (!string.IsNullOrWhiteSpace(data.Config.DefaultVm) && !data.Vms.ContainsKey(data.Config.DefaultVm))
        {
            throw new RetroBoxCatalogException($"Unknown default VM '{data.Config.DefaultVm}'.");
        }

        foreach (var (id, vm) in data.Vms)
        {
            id.RequireCatalogId("VM ID");
            vm.Label.RequireCatalogValue($"VM '{id}' label");
            vm.Path.RequireCatalogValue($"VM '{id}' path");
        }

        foreach (var (id, floppy) in data.Floppies)
        {
            id.RequireCatalogId("floppy ID");
            floppy.Label.RequireCatalogValue($"Floppy '{id}' label");
            floppy.Image.RequireCatalogValue($"Floppy '{id}' image");
            floppy.Size.RequireCatalogValue($"Floppy '{id}' size");

            if (!File.Exists(floppy.Image))
            {
                throw new RetroBoxCatalogException($"Floppy '{id}' image path '{floppy.Image}' does not exist.");
            }

            if (!RetroBoxFloppyCatalogRules.IsValidMode(floppy.Mode))
            {
                throw new RetroBoxCatalogException($"Invalid floppy mode '{floppy.Mode}' for floppy '{id}'.");
            }

            if (!RetroBoxFloppyCatalogRules.IsValidSize(floppy.Size))
            {
                throw new RetroBoxCatalogException($"Invalid floppy size '{floppy.Size}' for floppy '{id}'.");
            }
        }
    }

}
