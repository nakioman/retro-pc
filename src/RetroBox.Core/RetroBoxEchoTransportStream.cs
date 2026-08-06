using System.Text;
using System.Text.Json;

namespace RetroBox.Core;

internal sealed class RetroBoxEchoTransportStream : Stream
{
    private readonly TextWriter output;
    private readonly MemoryStream requestBuffer = new();
    private MemoryStream? responseBuffer;

    public RetroBoxEchoTransportStream(TextWriter output)
    {
        this.output = output;
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
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        requestBuffer.Write(buffer, offset, count);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        requestBuffer.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        requestBuffer.WriteAsync(buffer, cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        EnsureResponseReady();
        return responseBuffer!.Read(buffer, offset, count);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        EnsureResponseReady();
        return responseBuffer!.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureResponseReady();
        return responseBuffer!.ReadAsync(buffer, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    private void EnsureResponseReady()
    {
        if (responseBuffer is not null)
        {
            return;
        }

        var request = Encoding.UTF8.GetString(requestBuffer.ToArray());
        output.Write(request);
        output.Flush();
        responseBuffer = BuildResponse(request);
        responseBuffer.Position = 0;
    }

    private static MemoryStream BuildResponse(string request)
    {
        try
        {
            using var document = JsonDocument.Parse(request);
            var root = document.RootElement;
            var command = root.GetProperty("command").GetString() ?? string.Empty;
            var parameters = root.GetProperty("params");
            var drive = parameters.TryGetProperty("drive", out var driveElement)
                && driveElement.ValueKind == JsonValueKind.Number
                ? driveElement.GetInt32()
                : 0;
            var path = parameters.TryGetProperty("path", out var pathElement)
                && pathElement.ValueKind == JsonValueKind.String
                ? pathElement.GetString()
                : null;
            var readOnly = parameters.TryGetProperty("read_only", out var readOnlyElement)
                && readOnlyElement.ValueKind == JsonValueKind.True;

            var status = command switch
            {
                "floppy.insert" => new RetroBoxFloppyStatus(drive, true, path, readOnly, false, true),
                "floppy.eject" => new RetroBoxFloppyStatus(drive, false, null, false, false, true),
                _ => new RetroBoxFloppyStatus(drive, false, null, false, false, false),
            };

            var result = JsonSerializer.Serialize(status, RetroBoxFloppyJsonContext.Default.RetroBoxFloppyStatus);
            return new MemoryStream(Encoding.UTF8.GetBytes($$"""{"ok":true,"result":{{result}}}""" + "\n"));
        }
        catch (JsonException)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes("{\"ok\":true,\"result\":{}}\n"));
        }
    }
}
