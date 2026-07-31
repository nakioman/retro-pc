using System.Diagnostics;

namespace RetroBox.Core;

public sealed record RetroBoxBootRequest(
    string BinaryPath,
    string VmId,
    string VmPath,
    string RomPath);

public static class RetroBoxBoot
{
    public const string DefaultBinaryPath = "/opt/86Box/86box.AppImage";
    public const string DefaultRomPath = "/opt/86Box/roms";

    public static int Run(RetroBoxBootRequest request)
    {
        var startInfo = new ProcessStartInfo(request.BinaryPath)
        {
            WorkingDirectory = request.VmPath,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--vmpath");
        startInfo.ArgumentList.Add(request.VmPath);
        startInfo.ArgumentList.Add("--rompath");
        startInfo.ArgumentList.Add(request.RomPath);

        using var process = Process.Start(startInfo)
            ?? throw new IOException($"Could not start 86Box binary '{request.BinaryPath}'.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
