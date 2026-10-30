namespace RetroBox.Core;

public sealed record RetroBox86BoxLaunchRequest(
    string BinaryPath,
    string VmId,
    string VmPath,
    string RomPath);

public static class RetroBox86BoxLauncher
{
    public const string DefaultBinaryPath = "/opt/86Box/86box.AppImage";
    public const string DefaultRomPath = "/opt/86Box/roms";

    public static int Run(RetroBox86BoxLaunchRequest request, IRetroBoxProcessRunner? runner = null)
    {
        return (runner ?? new RetroBoxProcessRunner()).Run(new RetroBoxProcessSpec(
            request.BinaryPath,
            ["--vmpath", request.VmPath, "--rompath", request.RomPath])
        {
            WorkingDirectory = request.VmPath,
            // 86Box prints startup diagnostics to stdout; discard it so it never
            // reaches the appliance terminal (stderr still flows to the journal).
            DiscardStandardOutput = true,
        });
    }
}
