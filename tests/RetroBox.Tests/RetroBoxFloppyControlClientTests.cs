using System.Text;
using System.Text.Json;
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxFloppyControlClientTests
{
    [Fact]
    public async Task StatusAsync_parses_success_response()
    {
        var server = new ScriptedFloppySocket(
            """
            {"id":"req-1","ok":true,"result":{"drive":0,"inserted":true,"path":"/data/floppies/test.img","read_only":true,"busy":false,"changed":true}}

            """);
        var client = new RetroBoxFloppyControlClient(server.OpenAsync);

        var status = await client.StatusAsync(0);

        Assert.Equal(0, status.Drive);
        Assert.True(status.Inserted);
        Assert.Equal("/data/floppies/test.img", status.Path);
        Assert.True(status.ReadOnly);
        Assert.False(status.Busy);
        Assert.True(status.Changed);
    }

    [Fact]
    public async Task StatusAsync_throws_typed_exception_for_error_response()
    {
        var server = new ScriptedFloppySocket(
            """
            {"id":"req-1","ok":false,"error":{"code":"invalid_drive","message":"Drive must be an integer from 0 through 3.","details":{"drive":4}}}

            """);
        var client = new RetroBoxFloppyControlClient(server.OpenAsync);

        var error = await Assert.ThrowsAsync<RetroBoxFloppyControlException>(() => client.StatusAsync(4));

        Assert.Equal("invalid_drive", error.Code);
        Assert.Equal("Drive must be an integer from 0 through 3.", error.Message);
        Assert.Equal("""{"drive":4}""", error.DetailsJson);
    }

    [Fact]
    public async Task InsertAsync_writes_insert_request_as_one_json_line()
    {
        var server = new ScriptedFloppySocket(SuccessResponse());
        var client = new RetroBoxFloppyControlClient(server.OpenAsync);

        await client.InsertAsync(0, "/data/floppies/test.img", readOnly: true);

        Assert.EndsWith("\n", server.RequestText);
        Assert.Single(server.RequestText.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(server.RequestText);
        var root = document.RootElement;
        Assert.Equal("floppy.insert", root.GetProperty("command").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("id").GetString()));
        var parameters = root.GetProperty("params");
        Assert.Equal(0, parameters.GetProperty("drive").GetInt32());
        Assert.Equal("/data/floppies/test.img", parameters.GetProperty("path").GetString());
        Assert.True(parameters.GetProperty("read_only").GetBoolean());
    }

    [Fact]
    public async Task EjectAsync_writes_eject_request_as_one_json_line()
    {
        var server = new ScriptedFloppySocket(SuccessResponse(inserted: false, path: null, readOnly: false));
        var client = new RetroBoxFloppyControlClient(server.OpenAsync);

        await client.EjectAsync(0);

        Assert.EndsWith("\n", server.RequestText);
        using var document = JsonDocument.Parse(server.RequestText);
        var root = document.RootElement;
        Assert.Equal("floppy.eject", root.GetProperty("command").GetString());
        var parameters = root.GetProperty("params");
        Assert.Equal(0, parameters.GetProperty("drive").GetInt32());
        Assert.False(parameters.TryGetProperty("path", out _));
        Assert.False(parameters.TryGetProperty("read_only", out _));
    }

    [Fact]
    public async Task StatusAsync_writes_status_request_as_one_json_line()
    {
        var server = new ScriptedFloppySocket(SuccessResponse());
        var client = new RetroBoxFloppyControlClient(server.OpenAsync);

        await client.StatusAsync(0);

        Assert.EndsWith("\n", server.RequestText);
        using var document = JsonDocument.Parse(server.RequestText);
        var root = document.RootElement;
        Assert.Equal("floppy.status", root.GetProperty("command").GetString());
        var parameters = root.GetProperty("params");
        Assert.Equal(0, parameters.GetProperty("drive").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_empty_socket_path(string socketPath)
    {
        var error = Assert.Throws<ArgumentException>(() => new RetroBoxFloppyControlClient(socketPath));

        Assert.Contains("socket path", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string SuccessResponse(
        bool inserted = true,
        string? path = "/data/floppies/test.img",
        bool readOnly = true)
    {
        var insertedJson = JsonSerializer.Serialize(inserted);
        var pathJson = path is null ? "null" : JsonSerializer.Serialize(path);
        var readOnlyJson = JsonSerializer.Serialize(readOnly);
        return $"{{\"id\":\"req-1\",\"ok\":true,\"result\":{{\"drive\":0,\"inserted\":{insertedJson},\"path\":{pathJson},\"read_only\":{readOnlyJson},\"busy\":false,\"changed\":true}}}}\n";
    }

    private sealed class ScriptedFloppySocket(string response) : Stream
    {
        private readonly MemoryStream readStream = new(Encoding.UTF8.GetBytes(response));
        private readonly MemoryStream writeStream = new();

        public string RequestText => Encoding.UTF8.GetString(writeStream.ToArray());

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
}
