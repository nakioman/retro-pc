using System.IO.Ports;

namespace RetroBox.Core;

public interface IRetroBoxNfcClient
{
    Task<NfcResponse> PingAsync(CancellationToken cancellationToken = default);

    Task<NfcResponse> WriteAsync(string id, string mode, CancellationToken cancellationToken = default);
}

public abstract record NfcWriteResult
{
    public sealed record Written() : NfcWriteResult;

    public sealed record NotCataloged(string Id) : NfcWriteResult;

    public sealed record WriteFailed(string Message) : NfcWriteResult;
}

public sealed class NfcPortUnavailable : Exception
{
    public NfcPortUnavailable(string message)
        : base(message)
    {
    }

    public NfcPortUnavailable(string message, Exception inner)
        : base(message, inner)
    {
    }
}

public sealed class RetroBoxNfcSerialClient : IRetroBoxNfcClient
{
    private readonly Func<CancellationToken, Task<Stream>> streamFactory;

    internal RetroBoxNfcSerialClient(Func<CancellationToken, Task<Stream>> streamFactory)
    {
        this.streamFactory = streamFactory;
    }

    public RetroBoxNfcSerialClient(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ArgumentException("NFC serial port name is required.", nameof(portName));
        }

        streamFactory = ct =>
        {
            var port = new SerialPort(portName)
            {
                BaudRate = 115200,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                ReadTimeout = 2000,
                WriteTimeout = 2000,
            };

            try
            {
                port.Open();
                var stream = new SerialPortStream(port);
                return Task.FromResult<Stream>(stream);
            }
            catch
            {
                port.Dispose();
                throw;
            }
        };
    }

    public async Task<NfcResponse> PingAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteCommandAsync(
            RetroBoxArduinoSerialProtocol.BuildPingCommand(),
            cancellationToken);
    }

    public async Task<NfcResponse> WriteAsync(string id, string mode, CancellationToken cancellationToken = default)
    {
        return await ExecuteCommandAsync(
            RetroBoxArduinoSerialProtocol.BuildWriteCommand(id, mode),
            cancellationToken);
    }

    private async Task<NfcResponse> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await streamFactory(cancellationToken);
            using var writer = new StreamWriter(stream, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n",
            };

            await writer.WriteLineAsync(command.AsMemory(), cancellationToken);

            using var reader = new StreamReader(stream, leaveOpen: true);
            var line = await reader.ReadLineAsync(cancellationToken);

            return RetroBoxArduinoSerialProtocol.ParseResponse(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            throw new NfcPortUnavailable(
                $"NFC serial port is unavailable: {ex.Message}",
                ex);
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
}
