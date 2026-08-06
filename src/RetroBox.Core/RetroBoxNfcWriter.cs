namespace RetroBox.Core;

public sealed class RetroBoxNfcWriter
{
    private readonly IRetroBoxNfcClient client;
    private readonly RetroBoxConfigStore store;

    public RetroBoxNfcWriter(IRetroBoxNfcClient client, RetroBoxConfigStore store)
    {
        this.client = client;
        this.store = store;
    }

    public async Task<NfcWriteResult> WriteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var data = store.Load();

        if (!data.Floppies.TryGetValue(id, out var floppy))
        {
            return new NfcWriteResult.NotCataloged(id);
        }

        var response = await client.WriteAsync(id, floppy.Mode, cancellationToken);

        if (response is NfcResponse.Ok)
        {
            var updatedFloppies = new Dictionary<string, RetroBoxFloppy>(
                data.Floppies,
                StringComparer.Ordinal);
            updatedFloppies[id] = floppy with { Nfc = true };
            store.Save(data with { Floppies = updatedFloppies });
            return new NfcWriteResult.Written();
        }

        if (response is NfcResponse.Error error)
        {
            return new NfcWriteResult.WriteFailed(error.Message);
        }

        return new NfcWriteResult.WriteFailed(
            $"Unexpected NFC response: {response.GetType().Name}");
    }
}
