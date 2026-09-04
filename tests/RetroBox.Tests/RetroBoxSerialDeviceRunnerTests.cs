using System.Text;
using RetroBox.Daemon;

namespace RetroBox.Tests;

public sealed class RetroBoxSerialDeviceRunnerTests
{
    [Fact]
    public async Task OpenAsync_reports_a_missing_device_as_an_unavailable_serial_device()
    {
        // The controller-less appliance case, over the real SerialPort rather than a test stream:
        // the CLI can only degrade to "no controller" if every way a missing /dev node fails
        // arrives as RetroBoxSerialDeviceException, and the shape differs across platforms.
        var runner = new RetroBoxSerialDeviceRunner($"/dev/retrobox-missing-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<RetroBoxSerialDeviceException>(() => runner.OpenAsync());
    }

    [Fact]
    public async Task OpenReaderAsync_reads_lines_and_tolerates_crlf()
    {
        var runner = new RetroBoxSerialDeviceRunner(_ => Task.FromResult(
            LineStream("INSERT disk1,ro\nEJECT\r\nINIT 1\n")));

        using var reader = await runner.OpenReaderAsync();

        Assert.Equal("INSERT disk1,ro", await reader.ReadLineAsync());
        Assert.Equal("EJECT", await reader.ReadLineAsync());
        Assert.Equal("INIT 1", await reader.ReadLineAsync());
        Assert.Null(await reader.ReadLineAsync());
    }

    [Fact]
    public async Task OpenAsync_reads_events_and_writes_commands_over_one_stream()
    {
        var stream = new MemoryStream();
        var seed = Encoding.UTF8.GetBytes("INSERT disk1,ro\n");
        stream.Write(seed, 0, seed.Length);
        stream.Seek(0, SeekOrigin.Begin);
        var runner = new RetroBoxSerialDeviceRunner(_ => Task.FromResult<Stream>(stream));

        using var device = await runner.OpenAsync();

        Assert.Equal("INSERT disk1,ro", await device.Reader.ReadLineAsync());
        await device.Writer.WriteLineAsync("STATUS");
        await device.Writer.FlushAsync();

        stream.Seek(0, SeekOrigin.Begin);
        var content = new StreamReader(stream).ReadToEnd();
        Assert.Equal("INSERT disk1,ro\nSTATUS\n", content);
    }

    [Fact]
    public async Task OpenReaderAsync_reports_open_failure()
    {
        var runner = new RetroBoxSerialDeviceRunner(_ => throw new IOException("no such device"));

        var error = await Assert.ThrowsAsync<RetroBoxSerialDeviceException>(() => runner.OpenReaderAsync());

        Assert.Contains("no such device", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenReaderAsync_reports_device_disappearance()
    {
        var runner = new RetroBoxSerialDeviceRunner(_ => Task.FromResult<Stream>(new FaultingReadStream()));
        using var reader = await runner.OpenReaderAsync();

        var error = await Assert.ThrowsAsync<RetroBoxSerialDeviceException>(() => reader.ReadLineAsync());

        Assert.Contains("unavailable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("device disappeared", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenReaderAsync_cancellation_stops_reading_and_disposes_transport()
    {
        var stream = new TrackingStream();
        var runner = new RetroBoxSerialDeviceRunner(_ => Task.FromResult<Stream>(stream));
        using var reader = await runner.OpenReaderAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Null(await reader.ReadLineAsync(cancellation.Token));
        reader.Dispose();
        Assert.True(stream.Disposed);
    }

    [Fact]
    public void Constructor_rejects_empty_port_name()
    {
        Assert.Throws<ArgumentException>(() => new RetroBoxSerialDeviceRunner("  "));
    }

    private static Stream LineStream(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }

    private sealed class FaultingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("serial device disappeared");

        public override int Read(Span<byte> buffer) =>
            throw new IOException("serial device disappeared");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new IOException("serial device disappeared");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("serial device disappeared");

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();
    }

    private sealed class TrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
