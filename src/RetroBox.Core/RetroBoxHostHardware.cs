namespace RetroBox.Core;

/// <summary>Host devices detected by the installer (lib/hardware-detect.sh) into /etc/retrobox/hardware.env.</summary>
public static class RetroBoxHostHardware
{
    public const string DefaultEnvPath = "/etc/retrobox/hardware.env";

    /// <summary>
    /// The installer-detected optical drive, or null when none was detected or it is no longer
    /// attached: a missing device would stop QEMU from starting the VM at all.
    /// </summary>
    public static string? ReadOpticalDrive(string envPath = DefaultEnvPath)
    {
        var values = ReadEnv(envPath);
        return values.GetValueOrDefault("CDROM_STATUS") == "DETECTED"
            && values.GetValueOrDefault("CDROM_DEVICE") is { Length: > 0 } device
            && Path.Exists(device)
            ? device
            : null;
    }

    private static Dictionary<string, string> ReadEnv(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return values;
        }

        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf('=');
            if (separator > 0 && !line.TrimStart().StartsWith('#'))
            {
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return values;
    }
}
