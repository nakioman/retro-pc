using System.Globalization;
using System.Text.RegularExpressions;

namespace RetroBox.Core;

public sealed record RetroBoxQemuLaunchRequest(
    string QemuBinaryPath,
    string VmPath,
    string SessionExecutablePath)
{
    public string RunDirectory { get; init; } = RetroBoxQemuLauncher.DefaultRunDirectory;

    /// <summary>The installer-detected drive (<see cref="RetroBoxHostHardware"/>); null attaches none.</summary>
    public string? OpticalDrivePath { get; init; }

    public int VirtualTerminal { get; init; } = 1;
}

/// <summary>
/// Starts a QEMU VM fullscreen in a bare Xorg on the boot console. xinit runs the retrobox binary
/// itself as its client (<c>retrobox qemu-session</c>, see <see cref="RetroBoxQemuSession"/>); when
/// QEMU exits the client returns, X stops and the caller's selector takes the console back.
/// </summary>
public sealed partial class RetroBoxQemuLauncher(IRetroBoxProcessRunner runner)
{
    public const string DefaultRunDirectory = "/run/retrobox";
    public const string QmpSocketFileName = "qemu-qmp.sock";
    public const string SessionArgumentsFileName = "qemu-session.args";
    public const string SessionCommandName = "qemu-session";

    /// <summary>Fullscreen GTK scaled on the GPU; Ctrl+Alt+G releases the grabbed input.</summary>
    public const string KioskDisplay =
        "gtk,gl=on,full-screen=on,show-menubar=off,show-tabs=off,zoom-to-fit=on,grab-on-hover=on,window-close=off";

    /// <summary>Profile arguments describe the hardware; only host-specific ones are appended.</summary>
    public static IReadOnlyList<string> BuildQemuArguments(
        IReadOnlyList<string> profileArguments,
        RetroBoxQemuLaunchRequest request)
    {
        List<string> arguments =
        [
            .. profileArguments,
            "-display",
            KioskDisplay,
            // GTK shows each text console as a tab or window that would cover the VM.
            "-monitor",
            "none",
            "-serial",
            "none",
            "-parallel",
            "none",
            "-qmp",
            $"unix:{Path.Combine(request.RunDirectory, QmpSocketFileName)},server=on,wait=off",
        ];
        if (request.OpticalDrivePath is not null)
        {
            arguments.Add("-drive");
            arguments.Add($"file={request.OpticalDrivePath},if=ide,index=2,media=cdrom");
        }

        return arguments;
    }

    public int Run(RetroBoxQemuLaunchRequest request)
    {
        var profileArguments = RetroBoxQemuProfile.ReadArguments(
            Path.Combine(request.VmPath, RetroBoxQemuProfile.FileName));

        // The argv goes through a file: xinit splits client and server arguments at "--", and
        // the file doubles as a record of the last launch.
        Directory.CreateDirectory(request.RunDirectory);
        var argumentsFile = Path.Combine(request.RunDirectory, SessionArgumentsFileName);
        File.WriteAllText(argumentsFile, RetroBoxQemuProfile.Format(BuildQemuArguments(profileArguments, request)));

        return runner.Run(new RetroBoxProcessSpec(
            "xinit",
            [
                request.SessionExecutablePath,
                SessionCommandName,
                "--binary",
                request.QemuBinaryPath,
                "--args-file",
                argumentsFile,
                "--vm-path",
                request.VmPath,
                "--",
                ":0",
                $"vt{request.VirtualTerminal.ToString(CultureInfo.InvariantCulture)}",
                "-keeptty",
                "-nolisten",
                "tcp",
            ])
        {
            DiscardStandardOutput = true,
        });
    }

    /// <summary>X must start on the VT that runs the boot session (tty1 under retrobox-boot.service).</summary>
    public static int VirtualTerminalFromTty(string? ttyPath)
    {
        var match = ttyPath is null ? Match.Empty : ConsoleTty().Match(ttyPath);
        return match.Success && int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var vt) && vt > 0
            ? vt
            : 1;
    }

    public static string? ReadStandardInputTty()
    {
        try
        {
            return new FileInfo("/proc/self/fd/0").LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^/dev/tty(\d+)$")]
    private static partial Regex ConsoleTty();
}

public sealed record RetroBoxQemuSessionRequest(string QemuBinaryPath, string ArgumentsFile, string VmPath);

/// <summary>The xinit client: prepares the kiosk X session and runs QEMU until it exits.</summary>
public sealed class RetroBoxQemuSession(IRetroBoxProcessRunner runner)
{
    public int Run(RetroBoxQemuSessionRequest request)
    {
        var arguments = RetroBoxQemuProfile.ReadArguments(request.ArgumentsFile);

        runner.Run(new RetroBoxProcessSpec("xset", ["s", "off", "-dpms"]));
        using (runner.Start(new RetroBoxProcessSpec(
            "matchbox-window-manager",
            ["-use_titlebar", "no", "-use_cursor", "no"])))
        {
            return runner.Run(new RetroBoxProcessSpec(request.QemuBinaryPath, arguments)
            {
                WorkingDirectory = request.VmPath,
                Environment = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    // GTK's accessibility bridge waits on a D-Bus service the appliance does not run.
                    ["NO_AT_BRIDGE"] = "1",
                    // QEMU dlopens dxvk-native from the bundle.
                    ["LD_LIBRARY_PATH"] = RetroBoxQemuBundle.LibraryDirectory(request.QemuBinaryPath),
                },
                DiscardStandardOutput = true,
            });
        }
    }
}
