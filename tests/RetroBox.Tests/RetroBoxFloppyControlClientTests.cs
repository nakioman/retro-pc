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

        AssertRequest(server, "floppy.insert", parameters =>
        {
            Assert.Equal(0, parameters.GetProperty("drive").GetInt32());
            Assert.Equal("/data/floppies/test.img", parameters.GetProperty("path").GetString());
            Assert.True(parameters.GetProperty("read_only").GetBoolean());
        });
    }

    [Fact]
    public async Task EjectAsync_writes_eject_request_as_one_json_line()
    {
        var server = new ScriptedFloppySocket(SuccessResponse(inserted: false, path: null, readOnly: false));
        var client = new RetroBoxFloppyControlClient(server.OpenAsync);

        await client.EjectAsync(0);

        AssertRequest(server, "floppy.eject", parameters =>
        {
            Assert.Equal(0, parameters.GetProperty("drive").GetInt32());
            Assert.False(parameters.TryGetProperty("path", out _));
            Assert.False(parameters.TryGetProperty("read_only", out _));
        });
    }

    [Fact]
    public async Task StatusAsync_writes_status_request_as_one_json_line()
    {
        var server = new ScriptedFloppySocket(SuccessResponse());
        var client = new RetroBoxFloppyControlClient(server.OpenAsync);

        await client.StatusAsync(0);

        AssertRequest(server, "floppy.status", parameters =>
        {
            Assert.Equal(0, parameters.GetProperty("drive").GetInt32());
        });
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
        return JsonSerializer.Serialize(
            new
            {
                id = "req-1",
                ok = true,
                result = new
                {
                    drive = 0,
                    inserted,
                    path,
                    read_only = readOnly,
                    busy = false,
                    changed = true
                }
            }) + "\n";
    }

    private static void AssertRequest(
        ScriptedFloppySocket server,
        string expectedCommand,
        Action<JsonElement> assertParameters)
    {
        var requestText = server.RequestText;
        Assert.EndsWith("\n", requestText);
        Assert.Single(requestText.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        using var document = JsonDocument.Parse(requestText);
        var root = document.RootElement;
        Assert.Equal(expectedCommand, root.GetProperty("command").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("id").GetString()));
        assertParameters(root.GetProperty("params"));
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
