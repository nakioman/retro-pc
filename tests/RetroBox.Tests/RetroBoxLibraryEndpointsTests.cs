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

        // The cataloged filename is derived from the resolved ID and the (lowercased) extension,
        // not the raw upload name — see Post_floppies_uploads_the_same_filename_twice for why.
        Assert.True(File.Exists(Path.Combine(root, "cataloged", "monkey1.img")));
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
    public async Task Post_floppies_rejects_a_file_over_the_upload_limit()
    {
        await using var context = await StartAsync();

        using var response = await context.Client.PostAsync(
            "/api/floppies",
            BuildUpload("big.img", (int)RetroBoxLibraryEndpoints.MaxUploadBytes + 1));

        // Kestrel's own cap has 64 KiB of slack above MaxUploadBytes specifically so a request
        // this size clears Kestrel and reaches the handler's own file.Length check, which is what
        // returns a real {code, message} instead of a bodyless 413 from the framework.
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("file-too-large", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
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
    public async Task Post_floppies_uploads_the_same_filename_twice()
    {
        await using var context = await StartAsync();

        using var first = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img"));
        using var second = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img"));

        // Both uploads share an original filename. RetroBoxFloppyImporter targets
        // catalogedRoot/Path.GetFileName(source), so without deriving the scratch/cataloged
        // filename from the resolved ID, the second upload's File.Move would collide with the
        // first's cataloged/disk.img even though the two catalog IDs (disk, disk-2) don't collide.
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var catalog = await context.Client.GetStringAsync("/api/catalog");
        Assert.Contains("\"id\":\"disk\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"disk-2\"", catalog, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "cataloged", "disk.img")));
        Assert.True(File.Exists(Path.Combine(root, "cataloged", "disk-2.img")));
    }

    [Fact]
    public async Task Post_floppies_reports_a_scratch_name_collision_without_deleting_the_existing_file()
    {
        await using var context = await StartAsync();

        // The scratch root is also reachable directly over the LAN (the Samba share that is the
        // documented way images reach the appliance), so a file with the exact name this upload
        // will resolve to can already be sitting there before the request ever arrives.
        var preExisting = Path.Combine(root, "scratch", "disk.img");
        var preExistingContent = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(preExisting, preExistingContent);

        using var response = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("scratch-name-taken", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The assertion that actually matters: the file this request did not create is untouched.
        Assert.True(File.Exists(preExisting));
        Assert.Equal(preExistingContent, File.ReadAllBytes(preExisting));
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

    [Fact]
    public async Task Patch_does_not_misreport_a_load_failure_as_an_invalid_patch()
    {
        await using var context = await StartAsync();
        using (var upload = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img")))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        // Break a second, unrelated entry directly on disk (bypassing the API): its missing image
        // fails RetroBoxConfigStore.Validate for the whole catalog. Before RetroBoxUnknownFloppyException
        // and RetroBoxCatalogUnavailableException existed, the endpoint told apart 404 from
        // everything else by matching "Unknown floppy" on the exception message, so this load
        // failure fell into the same bucket as "the mode was invalid" and was reported as a 400
        // invalid-patch — blaming the client for a perfectly valid patch.
        var diskImage = Path.Combine(root, "cataloged", "disk.img");
        var missingImage = Path.Combine(root, "cataloged", "missing.img");
        File.WriteAllText(
            Path.Combine(root, "floppies.yaml"),
            "floppies:\n" +
            $"  disk:\n    label: disk\n    image: {diskImage}\n    mode: ro\n    size: 1.44M\n" +
            $"  broken:\n    label: broken\n    image: {missingImage}\n    mode: ro\n    size: 1.44M\n");

        using var patch = await context.Client.PatchAsync(
            "/api/floppies/disk",
            new StringContent("{\"mode\":\"rw\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.InternalServerError, patch.StatusCode);
        var body = await patch.Content.ReadAsStringAsync();
        Assert.Contains("catalog-unavailable", body, StringComparison.Ordinal);
        Assert.DoesNotContain("invalid-patch", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_floppies_does_not_misreport_a_load_failure_as_import_failed()
    {
        await using var context = await StartAsync();

        // Break an entry directly on disk (bypassing the API) before uploading anything: its
        // missing image fails RetroBoxConfigStore.Validate for the whole catalog.
        // RetroBoxFloppyImporter's own store.Load()/store.Save() would surface this as the same
        // plain RetroBoxCatalogException as a genuine "this upload was bad" failure.
        var missingImage = Path.Combine(root, "cataloged", "missing.img");
        File.WriteAllText(
            Path.Combine(root, "floppies.yaml"),
            $"floppies:\n  broken:\n    label: broken\n    image: {missingImage}\n    mode: ro\n    size: 1.44M\n");

        using var response = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("catalog-unavailable", body, StringComparison.Ordinal);
        Assert.DoesNotContain("import-failed", body, StringComparison.Ordinal);
    }

    private static MultipartFormDataContent BuildUpload(string fileName, int sizeBytes = 64)
    {
        var file = new ByteArrayContent(new byte[sizeBytes]);
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
