using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxBootTests
{
    [Fact]
    public void Defaults_use_appliance_runtime_paths()
    {
        Assert.Equal("/opt/86Box/86box.AppImage", RetroBoxBoot.DefaultBinaryPath);
        Assert.Equal("/opt/86Box/roms", RetroBoxBoot.DefaultRomPath);
    }

    [Fact]
    public void Run_passes_vm_and_rom_paths_and_uses_profile_as_working_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "retrobox-boot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "invocation.txt");
        var binary = Path.Combine(root, "86box-test.sh");
        var vmPath = Path.Combine(root, "vm");
        Directory.CreateDirectory(vmPath);
        File.WriteAllText(binary, $"#!/bin/sh\npwd > '{output}'\nprintf '%s\\n' \"$@\" >> '{output}'\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(binary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        try
        {
            Assert.Equal(0, RetroBoxBoot.Run(new RetroBoxBootRequest(
                binary, "pentium100", vmPath, "/opt/86Box/roms")));

            var lines = File.ReadAllLines(output);
            Assert.EndsWith($"{Path.DirectorySeparatorChar}vm", lines[0]);
            Assert.Equal(["--vmpath", vmPath, "--rompath", "/opt/86Box/roms"], lines[1..]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
