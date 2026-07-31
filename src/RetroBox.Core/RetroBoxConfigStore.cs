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

    private readonly IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDuplicateKeyChecking()
            .Build();
            
    private readonly ISerializer serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

    public RetroBoxCatalogData Load()
    {
        var config = LoadYaml<RetroBoxConfig>("config.yaml");
        var vms = LoadYaml<RetroBoxVmCatalog>("vms.yaml").Vms;
        var floppies = LoadYaml<RetroBoxFloppyCatalog>("floppies.yaml").Floppies;
        var games = LoadYaml<RetroBoxGameCatalog>("games.yaml").Games;

        var data = new RetroBoxCatalogData(config, vms, floppies, games);
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
            ("games.yaml", serializer.Serialize(new RetroBoxGameCatalog { Games = new Dictionary<string, RetroBoxGame>(data.Games, StringComparer.Ordinal) })),
        ]);
    }

    public void UpdateDefaultVm(string vmId)
    {
        var path = ResolvePath("config.yaml");
        if (!File.Exists(path))
        {
            throw new RetroBoxCatalogException($"Required YAML file '{path}' does not exist.");
        }

        var yaml = File.ReadAllText(path);
        var lines = yaml.Split("\n", StringSplitOptions.None);
        var replaced = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var newline = line.EndsWith('\r') ? "\r" : string.Empty;
            var content = newline.Length == 0 ? line : line[..^1];
            if (!content.StartsWith("defaultVm:", StringComparison.Ordinal))
            {
                continue;
            }

            var colon = content.IndexOf(':');
            var valueStart = colon + 1;
            var commentStart = content.IndexOf('#', valueStart);
            var valueEnd = commentStart < 0 ? content.Length : commentStart;
            var suffix = content[valueEnd..];
            var trailingWhitespace = content[valueStart..valueEnd].TrimEnd();
            var whitespace = content[valueStart..valueEnd][trailingWhitespace.Length..];
            lines[index] = content[..valueStart] + " " + vmId + whitespace + suffix + newline;
            replaced = true;
            break;
        }

        if (!replaced)
        {
            throw new RetroBoxCatalogException($"YAML file '{path}' does not contain a top-level defaultVm entry.");
        }

        File.WriteAllText(path, string.Join("\n", lines));
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
        data.Config.DefaultVm.RequireCatalogId("default VM");
        if (!data.Vms.ContainsKey(data.Config.DefaultVm))
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

        foreach (var (id, game) in data.Games)
        {
            id.RequireCatalogId("game ID");
            game.Label.RequireCatalogValue($"Game '{id}' label");

            if (!string.IsNullOrWhiteSpace(game.DefaultVm) && !data.Vms.ContainsKey(game.DefaultVm))
            {
                throw new RetroBoxCatalogException($"Game '{id}' references unknown default VM '{game.DefaultVm}'.");
            }

            foreach (var floppyId in game.FloppyIds)
            {
                if (!data.Floppies.ContainsKey(floppyId))
                {
                    throw new RetroBoxCatalogException($"Game '{id}' references unknown floppy '{floppyId}'.");
                }
            }
        }
    }

}
