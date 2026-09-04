using RetroBox.Core;

namespace RetroBox.Daemon;

public enum RetroBoxFloppyEventHandlerAction
{
    Initialized,
    Inserted,
    Ejected,
    IgnoredError,
    Failed,
}

public sealed record RetroBoxFloppyEventHandlerResult(
    RetroBoxFloppyEventHandlerAction Action,
    string Message,
    RetroBoxFloppyStatus? Status);

public sealed class RetroBoxFloppyEventHandler(
    IRetroBoxCatalogSource catalogSource,
    IRetroBoxFloppyControlClient floppyControlClient)
{
    private const int Drive = 0;

    public Task<RetroBoxFloppyEventHandlerResult> HandleAsync(
        RetroBoxArduinoSerialEvent serialEvent,
        CancellationToken cancellationToken = default)
    {
        return serialEvent switch
        {
            RetroBoxArduinoInsertEvent insert => HandleInsertAsync(insert, cancellationToken),
            RetroBoxArduinoEjectEvent => HandleEjectAsync(cancellationToken),
            RetroBoxArduinoInitEvent init => Task.FromResult(
                new RetroBoxFloppyEventHandlerResult(
                    RetroBoxFloppyEventHandlerAction.Initialized,
                    $"Floppy controller initialized (version {init.Version}).",
                    null)),
            RetroBoxArduinoErrorEvent error => Task.FromResult(
                new RetroBoxFloppyEventHandlerResult(
                    RetroBoxFloppyEventHandlerAction.IgnoredError,
                    $"Arduino controller error: {error.Message}",
                    null)),
            _ => Task.FromResult(
                new RetroBoxFloppyEventHandlerResult(
                    RetroBoxFloppyEventHandlerAction.Failed,
                    $"Unsupported floppy event '{serialEvent.GetType().Name}'.",
                    null)),
        };
    }

    private async Task<RetroBoxFloppyEventHandlerResult> HandleInsertAsync(
        RetroBoxArduinoInsertEvent insert,
        CancellationToken cancellationToken)
    {
        var catalog = catalogSource.Current;

        if (!catalog.Floppies.TryGetValue(insert.Id, out var floppy))
        {
            return new RetroBoxFloppyEventHandlerResult(
                RetroBoxFloppyEventHandlerAction.Failed,
                $"Unknown floppy '{insert.Id}'.",
                null);
        }

        if (!floppy.Nfc)
        {
            // Writing a tag needs the serial port this daemon holds open exclusively, so the
            // message must not tell the operator to run 'retrobox nfc write' as they stand: on
            // the appliance that command cannot open the port while the service is running.
            return new RetroBoxFloppyEventHandlerResult(
                RetroBoxFloppyEventHandlerAction.Failed,
                $"Floppy '{insert.Id}' has no assigned tag, and this daemon holds the serial port. "
                + "To assign one, log in over SSH, stop retrobox-daemon.service, run "
                + $"'retrobox nfc write {insert.Id} --port <SERIAL_DEVICE from /etc/retrobox/daemon.env>', "
                + "then start the service again.",
                null);
        }

        if (insert.Mode == RetroBoxFloppyCatalogRules.ReadWriteMode
            && floppy.Mode != RetroBoxFloppyCatalogRules.ReadWriteMode)
        {
            return new RetroBoxFloppyEventHandlerResult(
                RetroBoxFloppyEventHandlerAction.Failed,
                $"Floppy '{insert.Id}' is not writable.",
                null);
        }

        var readOnly = insert.Mode != RetroBoxFloppyCatalogRules.ReadWriteMode;
        var status = await floppyControlClient.InsertAsync(Drive, floppy.Image, readOnly, cancellationToken);
        return new RetroBoxFloppyEventHandlerResult(
            RetroBoxFloppyEventHandlerAction.Inserted,
            $"Inserted floppy '{insert.Id}' into drive {Drive}.",
            status);
    }

    private async Task<RetroBoxFloppyEventHandlerResult> HandleEjectAsync(CancellationToken cancellationToken)
    {
        var status = await floppyControlClient.EjectAsync(Drive, cancellationToken);
        return new RetroBoxFloppyEventHandlerResult(
            RetroBoxFloppyEventHandlerAction.Ejected,
            $"Ejected floppy drive {Drive}.",
            status);
    }
}
