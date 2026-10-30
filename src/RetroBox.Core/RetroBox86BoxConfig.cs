using System.Globalization;
using System.Text.RegularExpressions;

namespace RetroBox.Core;

/// <summary>A hard disk declared in an 86Box .cfg; its size comes from the profile's geometry.</summary>
public sealed record RetroBox86BoxHardDisk(string Id, string File, long SizeBytes);

public static partial class RetroBox86BoxConfig
{
    public const string FileName = "86box.cfg";

    private const int SectorSize = 512;

    public static IReadOnlyList<RetroBox86BoxHardDisk> ReadHardDisks(string configPath)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(configPath))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        var disks = new List<RetroBox86BoxHardDisk>();
        foreach (var (key, value) in values.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var match = HardDiskFileKey().Match(key);
            if (!match.Success)
            {
                continue;
            }

            var id = match.Groups[1].Value;
            var parametersKey = $"hdd_{id}_parameters";
            var file = value.Trim('"');
            if (file.Length == 0)
            {
                throw new RetroBoxCatalogException($"'{configPath}' has an empty hdd_{id}_fn.");
            }

            if (!values.TryGetValue(parametersKey, out var parameters) || !TryParseGeometry(parameters, out var sectors))
            {
                throw new RetroBoxCatalogException(
                    $"'{configPath}' has no valid {parametersKey} (sectors, heads, cylinders) for '{file}'.");
            }

            disks.Add(new RetroBox86BoxHardDisk(id, file, sectors * SectorSize));
        }

        return disks;
    }

    private static bool TryParseGeometry(string parameters, out long sectors)
    {
        sectors = 1;
        var fields = parameters.Split(',');
        if (fields.Length < 3)
        {
            return false;
        }

        foreach (var field in fields[..3])
        {
            if (!long.TryParse(field.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
            {
                return false;
            }

            sectors *= value;
        }

        return true;
    }

    [GeneratedRegex(@"^hdd_(\d+)_fn$")]
    private static partial Regex HardDiskFileKey();
}
