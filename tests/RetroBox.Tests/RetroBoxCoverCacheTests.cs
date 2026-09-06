using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxCoverCacheTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-cover-cache-{Guid.NewGuid():N}");

    public RetroBoxCoverCacheTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task ReplaceAsync_times_out_when_the_download_body_stalls()
    {
        var cache = new RetroBoxCoverCache(
            Path.Combine(root, "covers"),
            new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root)),
            (_, _) => Task.FromResult<Stream>(new StalledReadStream()),
            TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.ReplaceAsync("doom", 42, new Uri("https://covers.example/doom.jpg"), CancellationToken.None));
    }

    private sealed class StalledReadStream : Stream
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

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
