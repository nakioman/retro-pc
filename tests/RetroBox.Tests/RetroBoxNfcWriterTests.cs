using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxNfcWriterTests
{
    [Fact]
    public async Task Write_read_only_floppy_succeeds_and_persists_nfc_flag()
    {
        var root = CreateValidRoot();
        var store = new RetroBoxConfigStore(root);
        var client = new RecordingNfcClient
        {
            WriteResponse = new NfcResponse.Ok(),
        };
        var writer = new RetroBoxNfcWriter(client, store);

        var result = await writer.WriteAsync("monkey1-disk1");

        Assert.IsType<NfcWriteResult.Written>(result);
        Assert.Contains("WRITE:monkey1-disk1:ro", client.Calls);
        var reloaded = new RetroBoxConfigStore(root).Load();
        Assert.True(reloaded.Floppies["monkey1-disk1"].Nfc);
    }

    [Fact]
    public async Task Write_read_write_floppy_succeeds_and_persists_nfc_flag()
    {
        var root = CreateValidRoot(mode: "rw");
        var store = new RetroBoxConfigStore(root);
        var client = new RecordingNfcClient
        {
            WriteResponse = new NfcResponse.Ok(),
        };
        var writer = new RetroBoxNfcWriter(client, store);

        var result = await writer.WriteAsync("monkey1-disk1");

        Assert.IsType<NfcWriteResult.Written>(result);
        Assert.Contains("WRITE:monkey1-disk1:rw", client.Calls);
        var reloaded = new RetroBoxConfigStore(root).Load();
        Assert.True(reloaded.Floppies["monkey1-disk1"].Nfc);
    }

    [Fact]
    public async Task Write_returns_not_cataloged_for_unknown_id_without_calling_client()
    {
        var root = CreateValidRoot();
        var store = new RetroBoxConfigStore(root);
        var client = new RecordingNfcClient();
        var writer = new RetroBoxNfcWriter(client, store);

        var result = await writer.WriteAsync("unknown-disk");

        var notCataloged = Assert.IsType<NfcWriteResult.NotCataloged>(result);
        Assert.Equal("unknown-disk", notCataloged.Id);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Write_returns_write_failed_and_does_not_flip_nfc_on_error()
    {
        var root = CreateValidRoot();
        var store = new RetroBoxConfigStore(root);
        var client = new RecordingNfcClient
        {
            WriteResponse = new NfcResponse.Error("tag not detected"),
        };
        var writer = new RetroBoxNfcWriter(client, store);

        var result = await writer.WriteAsync("monkey1-disk1");

        var writeFailed = Assert.IsType<NfcWriteResult.WriteFailed>(result);
        Assert.Equal("tag not detected", writeFailed.Message);
        var reloaded = new RetroBoxConfigStore(root).Load();
        Assert.False(reloaded.Floppies["monkey1-disk1"].Nfc);
    }

    [Fact]
    public async Task Write_propagates_NfcPortUnavailable_exception()
    {
        var root = CreateValidRoot();
        var store = new RetroBoxConfigStore(root);
        var client = new RecordingNfcClient
        {
            ThrowOnCall = new NfcPortUnavailable("port busy"),
        };
        var writer = new RetroBoxNfcWriter(client, store);

        await Assert.ThrowsAsync<NfcPortUnavailable>(
            () => writer.WriteAsync("monkey1-disk1"));
    }

    private static string CreateValidRoot(string mode = "ro")
    {
        var root = Path.Combine(Path.GetTempPath(), "retrobox-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var floppyImage = Path.Combine(root, "monkey_island_disk_1.img");
        File.WriteAllBytes(floppyImage, Array.Empty<byte>());

        File.WriteAllText(
            Path.Combine(root, "config.yaml"),
            """
            defaultVm: test-vm
            """);
        File.WriteAllText(
            Path.Combine(root, "vms.yaml"),
            """
            vms:
              test-vm:
                label: "Test VM"
                path: "/data/vms/test"
            """);
        File.WriteAllText(
            Path.Combine(root, "floppies.yaml"),
            $$"""
            floppies:
              monkey1-disk1:
                label: "Monkey Island - Disk 1"
                image: "{{floppyImage}}"
                mode: "{{mode}}"
                size: "720K"
            """);

        return root;
    }
}
