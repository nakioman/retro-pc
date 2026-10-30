namespace RetroBox.Core;

/// <summary>The QEMU build the appliance unpacks to /opt/qemu-retrobox (bin/, lib/ with dxvk-native).</summary>
public static class RetroBoxQemuBundle
{
    public const string DefaultSystemBinaryPath = "/opt/qemu-retrobox/bin/qemu-system-i386";
    public const string DefaultImgBinaryPath = "/opt/qemu-retrobox/bin/qemu-img";

    /// <summary>The bundle's binaries load their libraries from the sibling lib/ directory.</summary>
    public static string LibraryDirectory(string binaryPath)
    {
        var binDirectory = Path.GetDirectoryName(Path.GetFullPath(binaryPath)) ?? "/";
        return Path.GetFullPath(Path.Combine(binDirectory, "..", "lib"));
    }
}
