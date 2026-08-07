using System.Diagnostics;
using System.IO;
using RetroBox.Core;

namespace RetroBox.Cli;

public interface IBootSplash
{
    void Quit();

    void Cover();
}

/// <summary>
/// Keeps the Plymouth splash on screen until 86Box takes over the display, so
/// no boot or 86Box text is visible between the splash and the first VM frame.
/// Every command is best-effort: outside the appliance (or without plymouth)
/// the calls fail silently and the terminal is simply left usable.
/// </summary>
public sealed class PlymouthBootSplash : IBootSplash
{
    public void Quit() => Run("--quit");

    public void Cover() => Run("--quit", "--retain-splash");

    private static void Run(params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("/usr/bin/sudo")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add("/usr/sbin/plymouth");
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            _ = stdout.GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // Plymouth only exists on the appliance console.
        }
    }
}

/// <summary>
/// Releases the boot splash before the terminal selector renders, so the ASCII
/// menu is visible instead of hidden behind a retained splash frame.
/// </summary>
public sealed class SplashQuittingSelectorUi(
    IRetroBoxBootSelectorUi inner,
    IBootSplash splash) : IRetroBoxBootSelectorUi
{
    public RetroBoxBootSelectionDecision Select(
        IReadOnlyList<KeyValuePair<string, RetroBoxVm>> virtualMachines,
        string? defaultVmId)
    {
        splash.Quit();
        return inner.Select(virtualMachines, defaultVmId);
    }
}
