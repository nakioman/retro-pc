using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxQmpClientTests
{
    private const string Greeting = """{"QMP": {"version": {"qemu": {"micro": 1, "minor": 1, "major": 11}, "package": ""}, "capabilities": ["oob"]}}""";

    [Fact]
    public async Task ConnectAsync_negotiates_capabilities_after_the_greeting()
    {
        var socket = new ScriptedQmpSocket(Greeting, """{"return": {}}""");

        await using var connection = await new RetroBoxQmpClient(socket.OpenAsync).ConnectAsync();

        var request = Assert.Single(socket.Requests);
        Assert.Equal("qmp_capabilities", request.GetProperty("execute").GetString());
    }

    [Fact]
    public async Task ConnectAsync_rejects_a_peer_that_is_not_qmp()
    {
        var socket = new ScriptedQmpSocket("""{"id":"req-1","ok":true}""");

        var error = await Assert.ThrowsAsync<RetroBoxQmpException>(
            () => new RetroBoxQmpClient(socket.OpenAsync).ConnectAsync());

        Assert.Equal("ProtocolError", error.ErrorClass);
    }

    [Fact]
    public async Task ConnectAsync_reports_an_unreachable_socket_as_unavailable()
    {
        var client = new RetroBoxQmpClient(_ => throw new SocketException((int)SocketError.ConnectionRefused));

        await Assert.ThrowsAsync<RetroBoxQmpUnavailableException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task Missing_socket_file_is_unavailable()
    {
        // Unix socket paths are short (104 bytes on macOS), so no nested temp directory.
        var client = new RetroBoxQmpClient(Path.Combine(Path.GetTempPath(), $"qmp-{Guid.NewGuid().ToString("N")[..8]}.sock"));

        await Assert.ThrowsAsync<RetroBoxQmpUnavailableException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task ChangeMediumAsync_sends_blockdev_change_medium_as_one_json_line()
    {
        var socket = new ScriptedQmpSocket(Greeting, """{"return": {}}""", """{"return": {}}""");
        await using var connection = await new RetroBoxQmpClient(socket.OpenAsync).ConnectAsync();

        await connection.ChangeMediumAsync("ide1-cd0", "/dev/sr0", "raw", readOnly: true);

        var request = socket.Requests[1];
        Assert.Equal("blockdev-change-medium", request.GetProperty("execute").GetString());
        var arguments = request.GetProperty("arguments");
        Assert.Equal("ide1-cd0", arguments.GetProperty("device").GetString());
        Assert.Equal("/dev/sr0", arguments.GetProperty("filename").GetString());
        Assert.Equal("raw", arguments.GetProperty("format").GetString());
        Assert.Equal("read-only", arguments.GetProperty("read-only-mode").GetString());
        Assert.EndsWith("}\n", socket.RequestText, StringComparison.Ordinal);
        Assert.Equal(2, socket.RequestText.Count(character => character == '\n'));
    }

    [Fact]
    public async Task ChangeMediumAsync_keeps_the_image_writable_unless_read_only()
    {
        var socket = new ScriptedQmpSocket(Greeting, """{"return": {}}""", """{"return": {}}""");
        await using var connection = await new RetroBoxQmpClient(socket.OpenAsync).ConnectAsync();

        await connection.ChangeMediumAsync("floppy0", "/data/floppies/a.img", "raw", readOnly: false);

        Assert.Equal("read-write", socket.Requests[1].GetProperty("arguments").GetProperty("read-only-mode").GetString());
    }

    [Fact]
    public async Task EjectAsync_sends_eject_with_force()
    {
        var socket = new ScriptedQmpSocket(Greeting, """{"return": {}}""", """{"return": {}}""");
        await using var connection = await new RetroBoxQmpClient(socket.OpenAsync).ConnectAsync();

        await connection.EjectAsync("ide1-cd0", force: true);

        var request = socket.Requests[1];
        Assert.Equal("eject", request.GetProperty("execute").GetString());
        Assert.Equal("ide1-cd0", request.GetProperty("arguments").GetProperty("device").GetString());
        Assert.True(request.GetProperty("arguments").GetProperty("force").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_skips_events_while_waiting_for_the_return()
    {
        var socket = new ScriptedQmpSocket(
            Greeting,
            """{"return": {}}""",
            """{"timestamp": {"seconds": 1, "microseconds": 2}, "event": "DEVICE_TRAY_MOVED", "data": {"device": "ide1-cd0", "tray-open": true}}""",
            """{"return": [{"device": "ide1-cd0"}]}""");
        await using var connection = await new RetroBoxQmpClient(socket.OpenAsync).ConnectAsync();

        var result = await connection.ExecuteAsync("query-block");

        Assert.Equal("ide1-cd0", result[0].GetProperty("device").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_throws_the_qmp_error()
    {
        var socket = new ScriptedQmpSocket(
            Greeting,
            """{"return": {}}""",
            """{"error": {"class": "GenericError", "desc": "Could not open '/dev/sr0': No medium found"}}""");
        await using var connection = await new RetroBoxQmpClient(socket.OpenAsync).ConnectAsync();

        var error = await Assert.ThrowsAsync<RetroBoxQmpException>(
            () => connection.ChangeMediumAsync("ide1-cd0", "/dev/sr0", "raw", readOnly: true));

        Assert.Equal("GenericError", error.ErrorClass);
        Assert.Equal("Could not open '/dev/sr0': No medium found", error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_qemu_closes_the_socket()
    {
        var socket = new ScriptedQmpSocket(Greeting, """{"return": {}}""");
        await using var connection = await new RetroBoxQmpClient(socket.OpenAsync).ConnectAsync();

        var error = await Assert.ThrowsAsync<RetroBoxQmpException>(() => connection.EjectAsync("ide1-cd0", force: true));

        Assert.Equal("Disconnected", error.ErrorClass);
    }
}
