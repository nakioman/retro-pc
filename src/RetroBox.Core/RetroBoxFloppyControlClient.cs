using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

[assembly: InternalsVisibleTo("RetroBox.Tests")]

namespace RetroBox.Core;

public interface IRetroBoxFloppyControlClient
{
    Task<RetroBoxFloppyStatus> InsertAsync(
        int drive,
        string imagePath,
        bool readOnly,
        CancellationToken cancellationToken = default);

    Task<RetroBoxFloppyStatus> EjectAsync(
        int drive,
        CancellationToken cancellationToken = default);

    Task<RetroBoxFloppyStatus> StatusAsync(
        int drive,
        CancellationToken cancellationToken = default);
}

public sealed record RetroBoxFloppyStatus(
    int Drive,
    bool Inserted,
    string? Path,
    bool ReadOnly,
    bool Busy,
    bool Changed);

public sealed class RetroBoxFloppyControlException : Exception
{
    public RetroBoxFloppyControlException(string code, string message, string? detailsJson = null)
        : base(message)
    {
        Code = code;
        DetailsJson = detailsJson;
    }

    public string Code { get; }

    public string? DetailsJson { get; }
}

public sealed class RetroBoxFloppyControlClient : IRetroBoxFloppyControlClient
{
    private static readonly byte[] NewLine = [(byte)'\n'];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Func<CancellationToken, Task<Stream>> streamFactory;
    private long nextRequestId;

    internal RetroBoxFloppyControlClient(Func<CancellationToken, Task<Stream>> streamFactory)
    {
        this.streamFactory = streamFactory;
    }

    public RetroBoxFloppyControlClient(string socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            throw new ArgumentException("86Box floppy control socket path is required.", nameof(socketPath));
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

    public Task<RetroBoxFloppyStatus> InsertAsync(
        int drive,
        string imagePath,
        bool readOnly,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            "floppy.insert",
            new FloppyInsertParameters(drive, imagePath, readOnly),
            cancellationToken);
    }

    public Task<RetroBoxFloppyStatus> EjectAsync(
        int drive,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            "floppy.eject",
            new FloppyDriveParameters(drive),
            cancellationToken);
    }

    public Task<RetroBoxFloppyStatus> StatusAsync(
        int drive,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            "floppy.status",
            new FloppyDriveParameters(drive),
            cancellationToken);
    }

    private async Task<RetroBoxFloppyStatus> SendAsync(
        string command,
        object parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await streamFactory(cancellationToken);
            var request = new FloppyControlRequest(
                $"req-{Interlocked.Increment(ref nextRequestId)}",
                command,
                parameters);

            await JsonSerializer.SerializeAsync(stream, request, JsonOptions, cancellationToken);
            await stream.WriteAsync(NewLine, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            using var reader = new StreamReader(stream, leaveOpen: true);
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new RetroBoxFloppyControlException(
                    "internal_failure",
                    "86Box floppy control socket closed without a response.");
            }

            try
            {
                return ParseResponse(line);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new RetroBoxFloppyControlException(
                    "internal_failure",
                    $"86Box floppy control response is malformed: {ex.Message}");
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        {
            throw new RetroBoxFloppyControlException(
                "internal_failure",
                $"86Box floppy control socket is unavailable: {ex.Message}");
        }
    }

    private static RetroBoxFloppyStatus ParseResponse(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.GetProperty("ok").GetBoolean())
        {
            return root.GetProperty("result").Deserialize<RetroBoxFloppyStatus>(JsonOptions)
                ?? throw new RetroBoxFloppyControlException(
                    "internal_failure",
                    "86Box floppy control response result is empty.");
        }

        var error = root.GetProperty("error");
        var code = error.GetProperty("code").GetString() ?? "internal_failure";
        var message = error.GetProperty("message").GetString() ?? "86Box floppy control request failed.";
        var detailsJson = error.TryGetProperty("details", out var details) ? details.GetRawText() : null;
        throw new RetroBoxFloppyControlException(code, message, detailsJson);
    }

    private sealed record FloppyControlRequest(
        string Id,
        string Command,
        object Params);

    private sealed record FloppyInsertParameters(
        int Drive,
        string Path,
        bool ReadOnly);

    private sealed record FloppyDriveParameters(int Drive);
}
