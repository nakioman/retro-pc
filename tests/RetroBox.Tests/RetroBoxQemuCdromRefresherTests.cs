using System.Net.Sockets;
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxQemuCdromRefresherTests
{
    private const string Greeting = """{"QMP": {"version": {"qemu": {"micro": 1, "minor": 1, "major": 11}, "package": ""}, "capabilities": []}}""";
    private const string Ok = """{"return": {}}""";
    private const string Drive = "/dev/disk/by-id/ata-HL-DT-ST_DVDRAM_GH24NSD1";

    [Fact]
    public async Task Disc_present_reloads_the_installer_drive_into_the_vm()
    {
        var socket = new ScriptedQmpSocket(Greeting, Ok, Ok);
        var refresher = Refresher(socket, RetroBoxOpticalMedia.Present);

        var outcome = await refresher.RefreshAsync(Drive);

        Assert.Equal(RetroBoxCdromRefreshOutcome.Changed, outcome);
        var request = socket.Requests[1];
        Assert.Equal("blockdev-change-medium", request.GetProperty("execute").GetString());
        var arguments = request.GetProperty("arguments");
        Assert.Equal(RetroBoxQemuLauncher.OpticalDriveId, arguments.GetProperty("device").GetString());
        Assert.Equal(Drive, arguments.GetProperty("filename").GetString());
        Assert.Equal("raw", arguments.GetProperty("format").GetString());
        Assert.Equal("read-only", arguments.GetProperty("read-only-mode").GetString());
    }

    [Theory]
    [InlineData(RetroBoxOpticalMedia.Absent)]
    [InlineData(RetroBoxOpticalMedia.TrayOpen)]
    public async Task Empty_drive_ejects_the_vm_disc(RetroBoxOpticalMedia media)
    {
        var socket = new ScriptedQmpSocket(Greeting, Ok, Ok);
        var refresher = Refresher(socket, media);

        var outcome = await refresher.RefreshAsync(Drive);

        Assert.Equal(RetroBoxCdromRefreshOutcome.Ejected, outcome);
        var request = socket.Requests[1];
        Assert.Equal("eject", request.GetProperty("execute").GetString());
        Assert.Equal(RetroBoxQemuLauncher.OpticalDriveId, request.GetProperty("arguments").GetProperty("device").GetString());
        Assert.True(request.GetProperty("arguments").GetProperty("force").GetBoolean());
    }

    [Fact]
    public async Task Spinning_up_drive_is_waited_for_before_acting()
    {
        var socket = new ScriptedQmpSocket(Greeting, Ok, Ok);
        var probe = new SequenceProbe(RetroBoxOpticalMedia.NotReady, RetroBoxOpticalMedia.NotReady, RetroBoxOpticalMedia.Present);
        var delays = new List<TimeSpan>();
        var refresher = new RetroBoxQemuCdromRefresher(
            new RetroBoxQmpClient(socket.OpenAsync),
            probe,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var outcome = await refresher.RefreshAsync(Drive);

        Assert.Equal(RetroBoxCdromRefreshOutcome.Changed, outcome);
        Assert.Equal(3, probe.Calls);
        Assert.Equal([RetroBoxQemuCdromRefresher.SpinUpPollInterval, RetroBoxQemuCdromRefresher.SpinUpPollInterval], delays);
    }

    [Fact]
    public async Task Drive_that_never_becomes_ready_still_gets_the_change()
    {
        var socket = new ScriptedQmpSocket(Greeting, Ok, Ok);
        var probe = new SequenceProbe(RetroBoxOpticalMedia.NotReady);
        var refresher = new RetroBoxQemuCdromRefresher(new RetroBoxQmpClient(socket.OpenAsync), probe, (_, _) => Task.CompletedTask);

        var outcome = await refresher.RefreshAsync(Drive);

        Assert.Equal(RetroBoxCdromRefreshOutcome.Changed, outcome);
        Assert.Equal(
            (int)(RetroBoxQemuCdromRefresher.SpinUpTimeout / RetroBoxQemuCdromRefresher.SpinUpPollInterval) + 1,
            probe.Calls);
    }

    [Fact]
    public async Task No_running_qemu_vm_is_not_an_error()
    {
        var refresher = new RetroBoxQemuCdromRefresher(
            new RetroBoxQmpClient(_ => throw new SocketException((int)SocketError.ConnectionRefused)),
            new SequenceProbe(RetroBoxOpticalMedia.Present),
            (_, _) => Task.CompletedTask);

        Assert.Equal(RetroBoxCdromRefreshOutcome.NoQemuVm, await refresher.RefreshAsync(Drive));
    }

    [Fact]
    public async Task Qmp_errors_reach_the_caller()
    {
        var socket = new ScriptedQmpSocket(Greeting, Ok, """{"error": {"class": "DeviceNotFound", "desc": "Device 'ide1-cd0' not found"}}""");
        var refresher = Refresher(socket, RetroBoxOpticalMedia.Present);

        var error = await Assert.ThrowsAsync<RetroBoxQmpException>(() => refresher.RefreshAsync(Drive));

        Assert.Equal("DeviceNotFound", error.ErrorClass);
    }

    private static RetroBoxQemuCdromRefresher Refresher(ScriptedQmpSocket socket, RetroBoxOpticalMedia media) =>
        new(new RetroBoxQmpClient(socket.OpenAsync), new SequenceProbe(media), (_, _) => Task.CompletedTask);

    /// <summary>Reports each state in turn, then repeats the last one.</summary>
    private sealed class SequenceProbe(params RetroBoxOpticalMedia[] states) : IRetroBoxOpticalDriveProbe
    {
        public int Calls { get; private set; }

        public RetroBoxOpticalMedia Probe(string devicePath)
        {
            Assert.Equal(Drive, devicePath);
            return states[Math.Min(Calls++, states.Length - 1)];
        }
    }
}
