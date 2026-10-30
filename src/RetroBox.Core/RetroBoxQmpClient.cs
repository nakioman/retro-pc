using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RetroBox.Core;

/// <summary>A QMP command failed, or QEMU broke the protocol. <see cref="ErrorClass"/> is QMP's error class.</summary>
public sealed class RetroBoxQmpException(string errorClass, string message) : Exception(message)
{
    public string ErrorClass { get; } = errorClass;
}

/// <summary>No QEMU is listening on the QMP socket: no QEMU VM is running.</summary>
public sealed class RetroBoxQmpUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);

/// <summary>
/// Client for QEMU's QMP socket (<c>-qmp unix:...,server=on,wait=off</c>, see
/// <see cref="RetroBoxQemuLauncher"/>). QEMU serves one client at a time, so callers keep a
/// connection only as long as one operation needs it.
/// </summary>
public sealed class RetroBoxQmpClient
{
    private readonly Func<CancellationToken, Task<Stream>> streamFactory;

    internal RetroBoxQmpClient(Func<CancellationToken, Task<Stream>> streamFactory)
    {
        this.streamFactory = streamFactory;
    }

    public RetroBoxQmpClient(string socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            throw new ArgumentException("QMP socket path is required.", nameof(socketPath));
        }

        var endpoint = new UnixDomainSocketEndPoint(socketPath);
        streamFactory = async cancellationToken =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(endpoint, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
    }

    /// <summary>Connects and leaves capabilities negotiation mode, so commands can run.</summary>
    public async Task<RetroBoxQmpConnection> ConnectAsync(CancellationToken cancellationToken = default)
    {
        Stream stream;
        try
        {
            stream = await streamFactory(cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        {
            throw new RetroBoxQmpUnavailableException($"QEMU QMP socket is unavailable: {ex.Message}", ex);
        }

        var connection = new RetroBoxQmpConnection(stream);
        try
        {
            await connection.NegotiateAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

public sealed class RetroBoxQmpConnection : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly StreamReader reader;

    internal RetroBoxQmpConnection(Stream stream)
    {
        this.stream = stream;
        reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    }

    /// <summary>Opens a removable drive and inserts <paramref name="filename"/>; the guest sees eject + insert.</summary>
    public Task ChangeMediumAsync(
        string device,
        string filename,
        string format,
        bool readOnly,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            "blockdev-change-medium",
            writer =>
            {
                writer.WriteString("device", device);
                writer.WriteString("filename", filename);
                writer.WriteString("format", format);
                writer.WriteString("read-only-mode", readOnly ? "read-only" : "read-write");
            },
            cancellationToken);
    }

    public Task EjectAsync(string device, bool force, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            "eject",
            writer =>
            {
                writer.WriteString("device", device);
                writer.WriteBoolean("force", force);
            },
            cancellationToken);
    }

    /// <summary>Runs one command and returns its <c>return</c> value; asynchronous events are skipped.</summary>
    public async Task<JsonElement> ExecuteAsync(
        string command,
        Action<Utf8JsonWriter>? writeArguments = null,
        CancellationToken cancellationToken = default)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("execute", command);
            if (writeArguments is not null)
            {
                writer.WriteStartObject("arguments");
                writeArguments(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        buffer.Write("\n"u8);
        try
        {
            await stream.WriteAsync(buffer.WrittenMemory, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            throw Disconnected(ex);
        }

        while (true)
        {
            using var message = await ReadMessageAsync(cancellationToken);
            var root = message.RootElement;
            if (root.TryGetProperty("return", out var result))
            {
                return result.Clone();
            }

            if (root.TryGetProperty("error", out var error))
            {
                throw new RetroBoxQmpException(
                    error.TryGetProperty("class", out var errorClass) ? errorClass.GetString() ?? "GenericError" : "GenericError",
                    error.TryGetProperty("desc", out var description) ? description.GetString() ?? command : command);
            }

            if (!root.TryGetProperty("event", out _))
            {
                throw new RetroBoxQmpException("ProtocolError", $"Unexpected QMP message: {root.GetRawText()}");
            }
        }
    }

    internal async Task NegotiateAsync(CancellationToken cancellationToken)
    {
        using (var greeting = await ReadMessageAsync(cancellationToken))
        {
            if (!greeting.RootElement.TryGetProperty("QMP", out _))
            {
                throw new RetroBoxQmpException("ProtocolError", "The socket did not greet with QMP.");
            }
        }

        await ExecuteAsync("qmp_capabilities", cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        reader.Dispose();
        await stream.DisposeAsync();
    }

    private async Task<JsonDocument> ReadMessageAsync(CancellationToken cancellationToken)
    {
        string? line;
        try
        {
            line = await reader.ReadLineAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            throw Disconnected(ex);
        }

        if (line is null)
        {
            throw new RetroBoxQmpException("Disconnected", "QEMU closed the QMP socket.");
        }

        try
        {
            var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new RetroBoxQmpException("ProtocolError", $"Unexpected QMP message: {line}");
            }

            return document;
        }
        catch (JsonException ex)
        {
            throw new RetroBoxQmpException("ProtocolError", $"Malformed QMP message: {ex.Message}");
        }
    }

    private static RetroBoxQmpException Disconnected(Exception ex) =>
        new("Disconnected", $"QMP socket failed: {ex.Message}");
}
