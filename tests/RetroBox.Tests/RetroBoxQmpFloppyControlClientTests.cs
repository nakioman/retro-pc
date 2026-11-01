using System.Net.Sockets;
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxQmpFloppyControlClientTests
{
    private const string Greeting = """{"QMP": {"version": {"qemu": {"micro": 1, "minor": 1, "major": 11}, "package": ""}, "capabilities": ["oob"]}}""";
    private const string Ok = """{"return": {}}""";
    private const string Image = "/data/floppies/cataloged/monkey1-disk1.img";

    private const string HardDisk = """{"io-status": "ok", "device": "ide0-hd0", "locked": false, "removable": false, "inserted": {"ro": false, "drv": "qcow2", "file": "hdd.qcow2"}, "qdev": "/machine/unattached/device[23]", "type": "unknown"}""";

    private static string QueryBlock(string floppy) => $$"""{"return": [{{HardDisk}}, {{floppy}}]}""";

    private static string FloppyWith(bool readOnly) =>
        $$"""{"device": "floppy0", "locked": false, "removable": true, "inserted": {"ro": {{(readOnly ? "true" : "false")}}, "drv": "raw", "file": "{{Image}}"}, "qdev": "/machine/unattached/device[17]", "type": "unknown"}""";

    private const string EmptyFloppy = """{"device": "floppy0", "locked": false, "removable": true, "qdev": "/machine/unattached/device[17]", "type": "unknown"}""";

    [Theory]
    [InlineData(true, "read-only")]
    [InlineData(false, "read-write")]
    public async Task InsertAsync_changes_the_floppy0_medium_in_the_catalog_mode(bool readOnly, string mode)
    {
        var socket = new ScriptedQmpSocket(Greeting, Ok, Ok, QueryBlock(FloppyWith(readOnly)));
        var client = new RetroBoxQmpFloppyControlClient(new RetroBoxQmpClient(socket.OpenAsync));

        var status = await client.InsertAsync(0, Image, readOnly);

        var request = socket.Requests[1];
        Assert.Equal("blockdev-change-medium", request.GetProperty("execute").GetString());
        var arguments = request.GetProperty("arguments");
        Assert.Equal("floppy0", arguments.GetProperty("device").GetString());
        Assert.Equal(Image, arguments.GetProperty("filename").GetString());
        Assert.Equal("raw", arguments.GetProperty("format").GetString());
        Assert.Equal(mode, arguments.GetProperty("read-only-mode").GetString());
        Assert.Equal("query-block", socket.Requests[2].GetProperty("execute").GetString());
        Assert.Equal(new RetroBoxFloppyStatus(0, true, Image, readOnly, false, true), status);
    }

    [Fact]
    public async Task EjectAsync_force_ejects_floppy0()
    {
        var socket = new ScriptedQmpSocket(Greeting, Ok, Ok, QueryBlock(EmptyFloppy));
        var client = new RetroBoxQmpFloppyControlClient(new RetroBoxQmpClient(socket.OpenAsync));

        var status = await client.EjectAsync(0);

        var request = socket.Requests[1];
        Assert.Equal("eject", request.GetProperty("execute").GetString());
        Assert.Equal("floppy0", request.GetProperty("arguments").GetProperty("device").GetString());
        Assert.True(request.GetProperty("arguments").GetProperty("force").GetBoolean());
        Assert.Equal(new RetroBoxFloppyStatus(0, false, null, false, false, true), status);
    }

    [Fact]
    public async Task StatusAsync_reports_the_inserted_image()
    {
        var socket = new ScriptedQmpSocket(Greeting, Ok, QueryBlock(FloppyWith(readOnly: true)));
        var client = new RetroBoxQmpFloppyControlClient(new RetroBoxQmpClient(socket.OpenAsync));

        var status = await client.StatusAsync(0);

        Assert.Equal("query-block", socket.Requests[1].GetProperty("execute").GetString());
        Assert.Equal(new RetroBoxFloppyStatus(0, true, Image, true, false, false), status);
    }

    [Fact]
    public async Task StatusAsync_reports_an_empty_drive()
    {
        var socket = new ScriptedQmpSocket(Greeting, Ok, QueryBlock(EmptyFloppy));
        var client = new RetroBoxQmpFloppyControlClient(new RetroBoxQmpClient(socket.OpenAsync));

        var status = await client.StatusAsync(0);

        Assert.Equal(new RetroBoxFloppyStatus(0, false, null, false, false, false), status);
    }

    [Fact]
    public async Task Each_command_uses_its_own_connection()
    {
        var sockets = new Queue<ScriptedQmpSocket>(
        [
            new ScriptedQmpSocket(Greeting, Ok, QueryBlock(EmptyFloppy)),
            new ScriptedQmpSocket(Greeting, Ok, Ok, QueryBlock(FloppyWith(readOnly: true))),
        ]);
        var client = new RetroBoxQmpFloppyControlClient(new RetroBoxQmpClient(ct => sockets.Dequeue().OpenAsync(ct)));

        await client.StatusAsync(0);
        await client.InsertAsync(0, Image, readOnly: true);

        Assert.Empty(sockets);
    }

    [Fact]
    public async Task Drives_other_than_a_are_invalid_without_connecting()
    {
        var connected = false;
        var client = new RetroBoxQmpFloppyControlClient(new RetroBoxQmpClient(_ =>
        {
            connected = true;
            throw new InvalidOperationException();
        }));

        var error = await Assert.ThrowsAsync<RetroBoxFloppyControlException>(() => client.InsertAsync(1, Image, true));

        Assert.Equal("invalid_drive", error.Code);
        Assert.False(connected);
    }

    [Fact]
    public async Task InsertAsync_reports_an_image_qemu_cannot_open_as_missing()
    {
        var socket = new ScriptedQmpSocket(
            Greeting,
            Ok,
            """{"error": {"class": "GenericError", "desc": "Could not open '/data/floppies/cataloged/gone.img': No such file or directory"}}""");
        var client = new RetroBoxQmpFloppyControlClient(new RetroBoxQmpClient(socket.OpenAsync));

        var error = await Assert.ThrowsAsync<RetroBoxFloppyControlException>(
            () => client.InsertAsync(0, "/data/floppies/cataloged/gone.img", true));

        Assert.Equal("missing_image", error.Code);
        Assert.Contains("No such file", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InsertAsync_reports_a_vm_without_floppy0_as_invalid_drive()
    {
        var socket = new ScriptedQmpSocket(
            Greeting,
            Ok,
            """{"error": {"class": "DeviceNotFound", "desc": "Device 'floppy0' not found"}}""");
        var client = new RetroBoxQmpFloppyControlClient(new RetroBoxQmpClient(socket.OpenAsync));

        var error = await Assert.ThrowsAsync<RetroBoxFloppyControlException>(() => client.InsertAsync(0, Image, true));

        Assert.Equal("invalid_drive", error.Code);
    }

    [Fact]
    public async Task StatusAsync_reports_a_vm_without_floppy0_as_invalid_drive()
    {
        var socket = new ScriptedQmpSocket(Greeting, Ok, $$"""{"return": [{{HardDisk}}]}""");
        var client = new RetroBoxQmpFloppyControlClient(new RetroBoxQmpClient(socket.OpenAsync));

        var error = await Assert.ThrowsAsync<RetroBoxFloppyControlException>(() => client.StatusAsync(0));

        Assert.Equal("invalid_drive", error.Code);
    }

    [Fact]
    public async Task Other_qmp_errors_are_internal_failures()
    {
        var socket = new ScriptedQmpSocket(
            Greeting,
            Ok,
            """{"error": {"class": "GenericError", "desc": "Device is locked"}}""");
        var client = new RetroBoxQmpFloppyControlClient(new RetroBoxQmpClient(socket.OpenAsync));

        var error = await Assert.ThrowsAsync<RetroBoxFloppyControlException>(() => client.EjectAsync(0));

        Assert.Equal("internal_failure", error.Code);
        Assert.Contains("Device is locked", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_qemu_vm_is_unavailable()
    {
        var client = new RetroBoxQmpFloppyControlClient(
            new RetroBoxQmpClient(_ => throw new SocketException((int)SocketError.ConnectionRefused)));

        var error = await Assert.ThrowsAsync<RetroBoxFloppyControlUnavailableException>(() => client.StatusAsync(0));

        Assert.Equal("internal_failure", error.Code);
        Assert.Contains("QMP", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Qemu_closing_the_socket_is_unavailable()
    {
        var socket = new ScriptedQmpSocket(Greeting);
        var client = new RetroBoxQmpFloppyControlClient(new RetroBoxQmpClient(socket.OpenAsync));

        await Assert.ThrowsAsync<RetroBoxFloppyControlUnavailableException>(() => client.StatusAsync(0));
    }

    [Fact]
    public async Task A_monitor_held_past_the_command_timeout_is_unavailable()
    {
        var client = new RetroBoxQmpFloppyControlClient(
            new RetroBoxQmpClient(_ => Task.FromResult<Stream>(new SilentStream())),
            TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<RetroBoxFloppyControlUnavailableException>(() => client.StatusAsync(0));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_unavailable()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new RetroBoxQmpFloppyControlClient(
            new RetroBoxQmpClient(_ => Task.FromResult<Stream>(new SilentStream())));

        var status = client.StatusAsync(0, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => status);
    }

    /// <summary>A connection queued in QEMU's backlog: connected, but no greeting arrives.</summary>
    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }
}
