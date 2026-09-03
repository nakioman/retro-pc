using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Tests;

public sealed class RetroBoxFloppyEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_mounts_catalog_ro_as_read_only()
    {
        var client = new RecordingFloppyControlClient();
        var handler = new RetroBoxFloppyEventHandler(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode),
            client);

        var result = await handler.HandleAsync(
            new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        Assert.Equal(RetroBoxFloppyEventHandlerAction.Inserted, result.Action);
        Assert.NotNull(result.Status);
        Assert.Equal(0, result.Status.Drive);
        Assert.Equal("/data/floppies/disk1.img", result.Status.Path);
        Assert.True(result.Status.ReadOnly);
        Assert.Equal("insert:0:/data/floppies/disk1.img:True", Assert.Single(client.Calls));
    }

    [Fact]
    public async Task HandleAsync_mounts_catalog_rw_as_read_write_when_tag_requests_rw()
    {
        var client = new RecordingFloppyControlClient();
        var handler = new RetroBoxFloppyEventHandler(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadWriteMode),
            client);

        var result = await handler.HandleAsync(
            new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadWriteMode));

        Assert.Equal(RetroBoxFloppyEventHandlerAction.Inserted, result.Action);
        Assert.NotNull(result.Status);
        Assert.Equal(0, result.Status.Drive);
        Assert.Equal("/data/floppies/disk1.img", result.Status.Path);
        Assert.False(result.Status.ReadOnly);
        Assert.Equal("insert:0:/data/floppies/disk1.img:False", Assert.Single(client.Calls));
    }

    [Fact]
    public async Task HandleAsync_mounts_catalog_rw_as_read_only_when_tag_requests_ro()
    {
        var client = new RecordingFloppyControlClient();
        var handler = new RetroBoxFloppyEventHandler(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadWriteMode),
            client);

        var result = await handler.HandleAsync(
            new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        Assert.Equal(RetroBoxFloppyEventHandlerAction.Inserted, result.Action);
        Assert.Equal("insert:0:/data/floppies/disk1.img:True", Assert.Single(client.Calls));
    }

    [Fact]
    public async Task HandleAsync_rejects_rw_tag_for_catalog_ro_without_calling_86box()
    {
        var client = new RecordingFloppyControlClient();
        var handler = new RetroBoxFloppyEventHandler(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode),
            client);

        var result = await handler.HandleAsync(
            new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadWriteMode));

        Assert.Equal(RetroBoxFloppyEventHandlerAction.Failed, result.Action);
        Assert.Contains("not writable", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task HandleAsync_rejects_unknown_id_without_calling_86box()
    {
        var client = new RecordingFloppyControlClient();
        var handler = new RetroBoxFloppyEventHandler(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadWriteMode),
            client);

        var result = await handler.HandleAsync(
            new RetroBoxArduinoInsertEvent("missing", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        Assert.Equal(RetroBoxFloppyEventHandlerAction.Failed, result.Action);
        Assert.Contains("Unknown floppy 'missing'", result.Message, StringComparison.Ordinal);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task HandleAsync_ejects_drive_zero()
    {
        var client = new RecordingFloppyControlClient();
        var handler = new RetroBoxFloppyEventHandler(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadWriteMode),
            client);

        var result = await handler.HandleAsync(new RetroBoxArduinoEjectEvent());

        Assert.Equal(RetroBoxFloppyEventHandlerAction.Ejected, result.Action);
        Assert.NotNull(result.Status);
        Assert.Equal(0, result.Status.Drive);
        Assert.False(result.Status.Inserted);
        Assert.Null(result.Status.Path);
        Assert.Equal("eject:0", Assert.Single(client.Calls));
    }

    [Fact]
    public async Task HandleAsync_records_controller_error_without_calling_86box()
    {
        var client = new RecordingFloppyControlClient();
        var handler = new RetroBoxFloppyEventHandler(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadWriteMode),
            client);

        var result = await handler.HandleAsync(new RetroBoxArduinoErrorEvent("tag read failed"));

        Assert.Equal(RetroBoxFloppyEventHandlerAction.IgnoredError, result.Action);
        Assert.Contains("tag read failed", result.Message, StringComparison.Ordinal);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task HandleAsync_refuses_to_mount_a_floppy_with_no_assigned_tag()
    {
        var client = new RecordingFloppyControlClient();
        var handler = new RetroBoxFloppyEventHandler(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode, nfc: false),
            client);

        var result = await handler.HandleAsync(
            new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        Assert.Equal(RetroBoxFloppyEventHandlerAction.Failed, result.Action);
        Assert.Contains("has no assigned tag", result.Message, StringComparison.Ordinal);
        Assert.Contains("retrobox nfc write", result.Message, StringComparison.Ordinal);
        Assert.Empty(client.Calls);
    }

    private static RetroBoxCatalogData CreateCatalog(
        string floppyId,
        string imagePath,
        string mode,
        bool nfc = true)
    {
        return FloppyControlTestCatalogs.CreateCatalog(floppyId, imagePath, mode, nfc);
    }
}
