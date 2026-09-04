using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxLibraryEndpointsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-endpoints-{Guid.NewGuid():N}");

    public RetroBoxLibraryEndpointsTests()
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "scratch"));
        Directory.CreateDirectory(Path.Combine(root, "cataloged"));
        File.WriteAllText(Path.Combine(root, "config.yaml"), "defaultVm: dos\n");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), $"vms:\n  dos:\n    label: DOS\n    path: {root}\n");
        File.WriteAllText(Path.Combine(root, "floppies.yaml"), "floppies: {}\n");
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
    public async Task Post_floppies_imports_the_upload_and_it_appears_in_the_catalog()
    {
        await using var context = await StartAsync();

        using var response = await context.Client.PostAsync("/api/floppies", BuildUpload("MONKEY1.IMG"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("monkey1", await context.Client.GetStringAsync("/api/catalog"), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "cataloged", "MONKEY1.IMG")));
    }

    [Fact]
    public async Task Post_floppies_rejects_an_unsupported_extension()
    {
        await using var context = await StartAsync();

        using var response = await context.Client.PostAsync("/api/floppies", BuildUpload("notes.txt"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unsupported-extension", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_floppies_suffixes_a_colliding_id()
    {
        await using var context = await StartAsync();

        using var first = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img"));
        using var second = await context.Client.PostAsync("/api/floppies", BuildUpload("DISK.ima"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var catalog = await context.Client.GetStringAsync("/api/catalog");
        Assert.Contains("\"id\":\"disk\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"disk-2\"", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_floppy_removes_it_from_the_catalog()
    {
        await using var context = await StartAsync();
        using (var upload = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img")))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        using var response = await context.Client.DeleteAsync("/api/floppies/disk");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain("\"id\":\"disk\"", await context.Client.GetStringAsync("/api/catalog"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_unknown_floppy_returns_not_found()
    {
        await using var context = await StartAsync();

        using var response = await context.Client.DeleteAsync("/api/floppies/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("unknown-floppy", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_floppy_changing_the_mode_clears_nfc()
    {
        await using var context = await StartAsync();
        using (var upload = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img")))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        using var patch = await context.Client.PatchAsync(
            "/api/floppies/disk",
            new StringContent("{\"mode\":\"rw\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        var catalog = await context.Client.GetStringAsync("/api/catalog");
        Assert.Contains("\"mode\":\"rw\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"nfc\":false", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_floppy_rejects_an_invalid_mode()
    {
        await using var context = await StartAsync();
        using (var upload = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img")))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        using var patch = await context.Client.PatchAsync(
            "/api/floppies/disk",
            new StringContent("{\"mode\":\"rx\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    private static MultipartFormDataContent BuildUpload(string fileName)
    {
        var file = new ByteArrayContent(new byte[64]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private async Task<EndpointContext> StartAsync()
    {
        var store = new RetroBoxConfigStore(root);
        var source = new RetroBoxWatchingCatalogSource(root, store.Load(), watchFileSystem: false);
        var host = await RetroBoxWebHost.StartAsync(
            new RetroBoxWebOptions
            {
                Port = 0,
                ConfigRoot = root,
                ScratchRoot = Path.Combine(root, "scratch"),
                CatalogedRoot = Path.Combine(root, "cataloged"),
            },
            source);

        return new EndpointContext(host, source, new HttpClient { BaseAddress = host.BaseAddress });
    }

    private sealed record EndpointContext(
        RetroBoxWebHost Host,
        RetroBoxWatchingCatalogSource Source,
        HttpClient Client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Host.DisposeAsync();
            Source.Dispose();
        }
    }
}
