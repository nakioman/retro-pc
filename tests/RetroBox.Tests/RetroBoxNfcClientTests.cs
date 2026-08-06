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

    // Regression: the inner StreamWriter/StreamReader must leave the underlying
    // stream open so the writer's disposal-time Flush does not reach into a
    // SerialPort whose BaseStream is only available while the port is open.
    // Reproduces the InvalidOperationException raised by SerialPort.BaseStream
    // when the reader (default leaveOpen:false) has already disposed the stream.
    [Fact]
    public async Task Ping_does_not_flush_stream_after_reader_disposes_it()
    {
        var stream = new PortLikeStream("PING\nPONG\n");

        var client = new RetroBoxNfcSerialClient(_ => Task.FromResult<Stream>(stream));
        var result = await client.PingAsync();

        Assert.IsType<NfcResponse.Pong>(result);
    }

    // Regression: many boards emit a boot banner (e.g. "READY ...") before
    // responding; non-protocol lines must be skipped until PONG arrives.
    [Fact]
    public async Task Ping_skips_boot_banner_before_pong()
    {
        var stream = new MemoryStream(Encoding.ASCII.GetBytes(
            "READY retrofloppy-esp8266 0.1\nPONG\n"));
        stream.Position = 0;

        var client = new RetroBoxNfcSerialClient(_ => Task.FromResult<Stream>(stream));
        var result = await client.PingAsync();

        Assert.IsType<NfcResponse.Pong>(result);
    }

    // Regression: a closed stream (EOF before any response) must surface as
    // Unknown rather than looping forever.
    [Fact]
    public async Task Ping_returns_unknown_when_stream_closes_without_response()
    {
        // Use an expandable MemoryStream (no fixed backing array) so the writer
        // can append the command, then the reader hits EOF without a response.
        var stream = new MemoryStream();

        var client = new RetroBoxNfcSerialClient(_ => Task.FromResult<Stream>(stream));
        var result = await client.PingAsync();

        Assert.IsType<NfcResponse.Unknown>(result);
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

    // Mimics SerialPort.BaseStream: Flush(), Read(), and Write() throw once the
    // underlying "port" has been disposed - the exact behavior that surfaced as
    // an unhandled InvalidOperationException at runtime.
    private sealed class PortLikeStream : Stream
    {
        private readonly MemoryStream inner;
        private bool portOpen = true;

        public PortLikeStream(string content)
        {
            var bytes = Encoding.ASCII.GetBytes(content);
            inner = new MemoryStream(bytes);
        }

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            if (!portOpen)
            {
                throw new InvalidOperationException(
                    "The BaseStream is only available when the port is open.");
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!portOpen)
            {
                throw new InvalidOperationException(
                    "The BaseStream is only available when the port is open.");
            }
            return inner.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!portOpen)
            {
                throw new InvalidOperationException(
                    "The BaseStream is only available when the port is open.");
            }
            inner.Write(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                portOpen = false;
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
