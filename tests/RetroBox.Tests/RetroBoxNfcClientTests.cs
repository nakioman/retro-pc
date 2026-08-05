using System.Text;
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxNfcClientTests
{
    [Fact]
    public async Task Ping_sends_command_and_parses_pong_response()
    {
        var stream = CreateTestStream("PING", "PONG");

        var client = new RetroBoxNfcSerialClient(_ => Task.FromResult<Stream>(stream));
        var result = await client.PingAsync();

        Assert.IsType<NfcResponse.Pong>(result);
    }

    [Fact]
    public async Task Write_sends_command_and_parses_ok_response()
    {
        var stream = CreateTestStream("WRITE disk1,ro", "OK");

        var client = new RetroBoxNfcSerialClient(_ => Task.FromResult<Stream>(stream));
        var result = await client.WriteAsync("disk1", "ro");

        Assert.IsType<NfcResponse.Ok>(result);
    }

    [Fact]
    public async Task Write_parses_error_response_when_firmware_reports_failure()
    {
        var stream = CreateTestStream("WRITE disk1,rw", "ERROR tag not detected");

        var client = new RetroBoxNfcSerialClient(_ => Task.FromResult<Stream>(stream));
        var result = await client.WriteAsync("disk1", "rw");

        var error = Assert.IsType<NfcResponse.Error>(result);
        Assert.Equal("tag not detected", error.Message);
    }

    [Fact]
    public async Task Ping_throws_NfcPortUnavailable_when_stream_fails()
    {
        var client = new RetroBoxNfcSerialClient(_ =>
            throw new IOException("Simulated port failure"));

        await Assert.ThrowsAsync<NfcPortUnavailable>(() => client.PingAsync());
    }

    private static Stream CreateTestStream(string commandLine, string responseLine)
    {
        var cmdBytes = Encoding.ASCII.GetBytes(commandLine + "\n");
        var respBytes = Encoding.ASCII.GetBytes(responseLine + "\n");
        var ms = new MemoryStream();
        ms.Write(cmdBytes, 0, cmdBytes.Length);
        ms.Write(respBytes, 0, respBytes.Length);
        ms.Position = 0;
        return ms;
    }
}
