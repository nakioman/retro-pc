using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("RetroBox.Tests")]

namespace RetroBox.Daemon;

public sealed class RetroBoxSerialDeviceException : Exception
{
    public RetroBoxSerialDeviceException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class RetroBoxSerialDeviceRunner
{
    private readonly Func<CancellationToken, Task<Stream>> streamFactory;

    public RetroBoxSerialDeviceRunner(string portName, int baud = RetroBoxSerialDeviceOptions.DefaultBaud)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ArgumentException("Floppy controller serial port name is required.", nameof(portName));
        }

        streamFactory = async ct =>
        {
            var port = new SerialPort(portName)
            {
                BaudRate = baud,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = 2000,
            };

            try
            {
                port.Open();
                return new SerialPortStream(port);
            }
            catch
            {
                port.Dispose();
                throw;
            }
        };
    }

    internal RetroBoxSerialDeviceRunner(Func<CancellationToken, Task<Stream>> streamFactory)
    {
        this.streamFactory = streamFactory
            ?? throw new ArgumentNullException(nameof(streamFactory));
    }

    public async Task<TextReader> OpenReaderAsync(CancellationToken cancellationToken = default)
    {
        var stream = await OpenStreamAsync(cancellationToken);
        return new SerialDeviceReader(stream);
    }

    public async Task<RetroBoxSerialDevice> OpenAsync(CancellationToken cancellationToken = default)
    {
        var stream = await OpenStreamAsync(cancellationToken);
        return new RetroBoxSerialDevice(stream, new SerialDeviceReader(stream), new SerialDeviceWriter(stream));
    }

    private async Task<Stream> OpenStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await streamFactory(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            throw new RetroBoxSerialDeviceException(
                $"Floppy controller serial device is unavailable: {ex.Message}",
                ex);
        }
    }

    private sealed class SerialDeviceReader : TextReader
    {
        private readonly StreamReader inner;

        public SerialDeviceReader(Stream stream)
        {
            inner = new StreamReader(stream);
        }

        public override Task<string?> ReadLineAsync()
        {
            return ReadLineAsync(CancellationToken.None).AsTask();
        }

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            try
            {
                return await inner.ReadLineAsync(cancellationToken);
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
            {
                throw new RetroBoxSerialDeviceException(
                    $"Floppy controller serial device is unavailable: {ex.Message}",
                    ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class SerialPortStream : Stream
    {
        private readonly SerialPort port;

        public SerialPortStream(SerialPort port)
        {
            this.port = port;
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

        public override void Flush() => port.BaseStream.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            port.BaseStream.Read(buffer, offset, count);

        public override void Write(byte[] buffer, int offset, int count) =>
            port.BaseStream.Write(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                port.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class SerialDeviceWriter : TextWriter
    {
        private readonly StreamWriter inner;

        public SerialDeviceWriter(Stream stream)
        {
            inner = new StreamWriter(stream)
            {
                AutoFlush = true,
                NewLine = "\n",
            };
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => inner.Write(value);

        public override void Write(string? value) => inner.Write(value);

        public override Task WriteAsync(string? value) => inner.WriteAsync(value);

        public override Task WriteLineAsync(string? value) => inner.WriteLineAsync(value);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Flush();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>A single opened serial device exposing one reader and one writer over the same port.</summary>
public sealed class RetroBoxSerialDevice : IDisposable
{
    private readonly Stream stream;

    internal RetroBoxSerialDevice(Stream stream, TextReader reader, TextWriter writer)
    {
        this.stream = stream;
        Reader = reader;
        Writer = writer;
    }

    public TextReader Reader { get; }

    public TextWriter Writer { get; }

    public void Dispose()
    {
        Writer.Flush();
        stream.Dispose();
    }
}
