using System.Globalization;

namespace RetroBox.Core;

public static class RetroBoxVmEngines
{
    public const string EightySixBox = "86box";
    public const string Qemu = "qemu";

    public static IReadOnlyList<string> All { get; } = [EightySixBox, Qemu];

    /// <summary>The engine's native profile file, the VM's hardware source of truth (ADR 0009).</summary>
    public static string ProfileFileName(string engine) =>
        engine == Qemu ? RetroBoxQemuProfile.FileName : RetroBox86BoxConfig.FileName;
}

public static class RetroBoxVmDiskFormats
{
    public const string Raw = "raw";
    public const string Qcow2 = "qcow2";

    public static IReadOnlyList<string> All { get; } = [Raw, Qcow2];
}

public static class RetroBoxVmCatalogRules
{
    public static bool TryParseDiskSize(string? value, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var shift = char.ToUpperInvariant(value[^1]) switch
        {
            'K' => 10,
            'M' => 20,
            'G' => 30,
            'T' => 40,
            _ => 0,
        };
        var digits = shift == 0 ? value : value[..^1];
        if (digits.Length == 0
            || !digits.All(char.IsAsciiDigit)
            || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number <= 0
            || number > long.MaxValue >> shift)
        {
            return false;
        }

        bytes = number << shift;
        return true;
    }

    internal static void Validate(string id, RetroBoxVm vm)
    {
        if (!RetroBoxVmEngines.All.Contains(vm.Engine))
        {
            throw new RetroBoxCatalogException(
                $"VM '{id}' engine '{vm.Engine}' must be one of: {string.Join(", ", RetroBoxVmEngines.All)}.");
        }

        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var disk in vm.Disks)
        {
            disk.File.RequireCatalogValue($"VM '{id}' disk file");
            if (Path.IsPathRooted(disk.File))
            {
                throw new RetroBoxCatalogException(
                    $"VM '{id}' disk '{disk.File}' must be relative to the VM path.");
            }

            if (disk.File.Split('/', '\\').Contains(".."))
            {
                throw new RetroBoxCatalogException(
                    $"VM '{id}' disk '{disk.File}' must stay inside the VM path.");
            }

            if (!files.Add(disk.File))
            {
                throw new RetroBoxCatalogException($"VM '{id}' declares disk '{disk.File}' twice.");
            }

            if (!RetroBoxVmDiskFormats.All.Contains(disk.Format))
            {
                throw new RetroBoxCatalogException(
                    $"VM '{id}' disk '{disk.File}' format '{disk.Format}' must be one of: {string.Join(", ", RetroBoxVmDiskFormats.All)}.");
            }

            if (vm.Engine == RetroBoxVmEngines.EightySixBox && disk.Format != RetroBoxVmDiskFormats.Raw)
            {
                throw new RetroBoxCatalogException(
                    $"VM '{id}' 86Box disk '{disk.File}' must be raw.");
            }

            if (disk.Size is not null && !TryParseDiskSize(disk.Size, out _))
            {
                throw new RetroBoxCatalogException(
                    $"VM '{id}' disk '{disk.File}' size '{disk.Size}' is invalid; use bytes or a K, M, G, or T suffix.");
            }

            if (vm.Engine == RetroBoxVmEngines.Qemu && disk.Size is null)
            {
                throw new RetroBoxCatalogException(
                    $"VM '{id}' QEMU disk '{disk.File}' needs a size.");
            }
        }
    }
}
