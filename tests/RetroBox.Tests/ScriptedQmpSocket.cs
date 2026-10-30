using System.Text;
using System.Text.Json;

namespace RetroBox.Tests;

/// <summary>A QMP peer whose replies are scripted up front; requests are captured.</summary>
internal sealed class ScriptedQmpSocket(params string[] responseLines) : Stream
{
    private readonly MemoryStream readStream = new(Encoding.UTF8.GetBytes(string.Concat(responseLines.Select(line => line + "\r\n"))));
    private readonly MemoryStream writeStream = new();

    public string RequestText => Encoding.UTF8.GetString(writeStream.ToArray());

    public IReadOnlyList<JsonElement> Requests => RequestText
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => JsonDocument.Parse(line).RootElement)
        .ToArray();

    public Task<Stream> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(this);
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => writeStream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => writeStream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => readStream.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return readStream.ReadAsync(buffer, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => writeStream.Write(buffer, offset, count);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return writeStream.WriteAsync(buffer, cancellationToken);
    }
}
