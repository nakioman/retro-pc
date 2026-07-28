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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Func<CancellationToken, Task<Stream>> streamFactory;

    internal RetroBoxFloppyControlClient(Func<CancellationToken, Task<Stream>> streamFactory)
    {
        this.streamFactory = streamFactory;
    }

    public RetroBoxFloppyControlClient(string socketPath)
    {
        streamFactory = _ => throw new NotImplementedException("Unix socket transport is added in Task 3.");
    }

    public Task<RetroBoxFloppyStatus> InsertAsync(
        int drive,
        string imagePath,
        bool readOnly,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Command serialization is added in Task 2.");
    }

    public Task<RetroBoxFloppyStatus> EjectAsync(
        int drive,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Command serialization is added in Task 2.");
    }

    public Task<RetroBoxFloppyStatus> StatusAsync(
        int drive,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(cancellationToken);
    }

    private async Task<RetroBoxFloppyStatus> SendAsync(CancellationToken cancellationToken)
    {
        await using var stream = await streamFactory(cancellationToken);
        using var reader = new StreamReader(stream, leaveOpen: true);
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            throw new RetroBoxFloppyControlException(
                "internal_failure",
                "86Box floppy control socket closed without a response.");
        }

        return ParseResponse(line);
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
}
