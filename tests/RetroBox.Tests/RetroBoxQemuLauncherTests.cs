using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxQemuLauncherTests : IDisposable
{
    // The Windows XP arguments proven in the Phase 0 spike (design doc, "the vmvga path").
    private const string SpikeProfile = """
        # Windows XP on vmvga (Phase 0 spike)
        -name
        Windows XP
        -machine
        pc,accel=kvm,vmport=on
        -cpu
        core2duo
        -smp
        2
        -m
        3072

        -rtc
        base=localtime
        -boot
        order=cd,menu=on,splash-time=3000
        -display
        gtk,gl=on,full-screen=on,show-menubar=off,show-tabs=off,zoom-to-fit=on,grab-on-hover=on,window-close=off
        -monitor
        none
        -serial
        none
        -parallel
        none
        -vga
        none
        -device
        vmvga,hwcursor=off,gbobjects=off,vgpu=9
        -drive
        file=xp.qcow2,if=ide,index=0,media=disk,format=qcow2
        -drive
        if=floppy,index=0
        -drive
        if=ide,index=3,media=cdrom
        -audiodev
        alsa,id=snd0
        -device
        AC97,audiodev=snd0
        -netdev
        user,id=net0
        -device
        rtl8139,netdev=net0
        """;

    private static readonly string[] SpikeArguments =
    [
        "-name", "Windows XP",
        "-machine", "pc,accel=kvm,vmport=on", "-cpu", "core2duo", "-smp", "2", "-m", "3072",
        "-rtc", "base=localtime", "-boot", "order=cd,menu=on,splash-time=3000",
        "-display", "gtk,gl=on,full-screen=on,show-menubar=off,show-tabs=off,zoom-to-fit=on,grab-on-hover=on,window-close=off",
        "-monitor", "none", "-serial", "none", "-parallel", "none",
        "-vga", "none", "-device", "vmvga,hwcursor=off,gbobjects=off,vgpu=9",
        "-drive", "file=xp.qcow2,if=ide,index=0,media=disk,format=qcow2",
        "-drive", "if=floppy,index=0",
        "-drive", "if=ide,index=3,media=cdrom",
        "-audiodev", "alsa,id=snd0", "-device", "AC97,audiodev=snd0",
        "-netdev", "user,id=net0", "-device", "rtl8139,netdev=net0",
    ];

    private readonly string root = Path.Combine(Path.GetTempPath(), "retrobox-qemu-tests", Guid.NewGuid().ToString("N"));

    public RetroBoxQemuLauncherTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "winxp"));
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Profile_reads_one_argument_per_line_skipping_comments_and_blanks()
    {
        var path = Path.Combine(root, "winxp", RetroBoxQemuProfile.FileName);
        File.WriteAllText(path, SpikeProfile + "\n   \n  -snapshot  \n");

        Assert.Equal([.. SpikeArguments, "-snapshot"], RetroBoxQemuProfile.ReadArguments(path));
    }

    [Fact]
    public void Profile_reports_missing_file()
    {
        var path = Path.Combine(root, "winxp", RetroBoxQemuProfile.FileName);

        var error = Assert.Throws<RetroBoxCatalogException>(() => RetroBoxQemuProfile.ReadArguments(path));

        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void Qemu_arguments_append_qmp_socket_without_optical_drive()
    {
        var arguments = RetroBoxQemuLauncher.BuildQemuArguments(SpikeArguments, Request());

        Assert.Equal(
            [.. SpikeArguments, "-qmp", $"unix:{Path.Combine(root, "run", "qemu-qmp.sock")},server=on,wait=off"],
            arguments);
    }

    [Fact]
    public void Qemu_arguments_attach_host_optical_drive_when_present()
    {
        var arguments = RetroBoxQemuLauncher.BuildQemuArguments(SpikeArguments, Request() with { OpticalDrivePath = "/dev/disk/by-id/ata-HL-DT-ST_DVDRAM" });

        Assert.Equal(["-drive", "file=/dev/disk/by-id/ata-HL-DT-ST_DVDRAM,if=ide,index=2,media=cdrom"], arguments.TakeLast(2));
    }

    [Fact]
    public void Run_writes_session_arguments_and_starts_xinit_with_retrobox_as_client()
    {
        File.WriteAllText(Path.Combine(root, "winxp", RetroBoxQemuProfile.FileName), SpikeProfile);
        var runner = new RecordingRunner(exitCode: 3);

        var exitCode = new RetroBoxQemuLauncher(runner).Run(Request() with { VirtualTerminal = 2 });

        Assert.Equal(3, exitCode);
        var argumentsFile = Path.Combine(root, "run", "qemu-session.args");
        Assert.Equal(
            RetroBoxQemuLauncher.BuildQemuArguments(SpikeArguments, Request()),
            RetroBoxQemuProfile.ReadArguments(argumentsFile));
        var run = Assert.Single(runner.Runs);
        Assert.Equal("xinit", run.FileName);
        Assert.Equal(
            [
                "/opt/retrobox/retrobox", "qemu-session",
                "--binary", "/opt/qemu-retrobox/bin/qemu-system-i386",
                "--args-file", argumentsFile,
                "--vm-path", Path.Combine(root, "winxp"),
                "--", ":0", "vt2", "-keeptty", "-nolisten", "tcp",
            ],
            run.Arguments);
        Assert.True(run.DiscardStandardOutput);
    }

    [Fact]
    public void Run_fails_before_starting_x_when_profile_is_missing()
    {
        var runner = new RecordingRunner();

        Assert.Throws<RetroBoxCatalogException>(() => new RetroBoxQemuLauncher(runner).Run(Request()));
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void Session_prepares_x_runs_qemu_and_stops_window_manager()
    {
        var argumentsFile = Path.Combine(root, "qemu-session.args");
        File.WriteAllText(argumentsFile, RetroBoxQemuProfile.Format(SpikeArguments));
        var runner = new RecordingRunner(exitCode: 0);

        var exitCode = new RetroBoxQemuSession(runner).Run(new RetroBoxQemuSessionRequest(
            "/opt/qemu-retrobox/bin/qemu-system-i386", argumentsFile, Path.Combine(root, "winxp")));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            ["run xset", "start matchbox-window-manager", "run qemu-system-i386", "stop matchbox-window-manager"],
            runner.Log);
        Assert.Equal(["s", "off", "-dpms"], runner.Runs[0].Arguments);
        Assert.Equal(["-use_titlebar", "no", "-use_cursor", "no"], runner.Runs[1].Arguments);
        var qemu = runner.Runs[2];
        Assert.Equal("/opt/qemu-retrobox/bin/qemu-system-i386", qemu.FileName);
        Assert.Equal(SpikeArguments, qemu.Arguments);
        Assert.Equal(Path.Combine(root, "winxp"), qemu.WorkingDirectory);
        Assert.Equal("1", qemu.Environment["NO_AT_BRIDGE"]);
        Assert.Equal("/opt/qemu-retrobox/lib", qemu.Environment["LD_LIBRARY_PATH"]);
        Assert.True(qemu.DiscardStandardOutput);
    }

    [Fact]
    public void Session_returns_qemu_exit_code()
    {
        var argumentsFile = Path.Combine(root, "qemu-session.args");
        File.WriteAllText(argumentsFile, "-m\n64\n");

        var exitCode = new RetroBoxQemuSession(new RecordingRunner(exitCode: 1)).Run(
            new RetroBoxQemuSessionRequest("/opt/qemu-retrobox/bin/qemu-system-i386", argumentsFile, root));

        Assert.Equal(1, exitCode);
    }

    [Theory]
    [InlineData("/dev/tty1", 1)]
    [InlineData("/dev/tty7", 7)]
    [InlineData("/dev/pts/0", 1)]
    [InlineData("/dev/tty0", 1)]
    [InlineData(null, 1)]
    public void Virtual_terminal_comes_from_the_console_tty(string? stdinTarget, int expected)
    {
        Assert.Equal(expected, RetroBoxQemuLauncher.VirtualTerminalFromTty(stdinTarget));
    }

    private RetroBoxQemuLaunchRequest Request() => new(
        "/opt/qemu-retrobox/bin/qemu-system-i386",
        Path.Combine(root, "winxp"),
        "/opt/retrobox/retrobox")
    {
        RunDirectory = Path.Combine(root, "run"),
    };
}
