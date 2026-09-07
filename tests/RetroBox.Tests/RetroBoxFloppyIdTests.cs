using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxFloppyIdTests
{
    [Fact]
    public void Create_returns_a_catalog_valid_id_that_fits_an_NFC_payload()
    {
        var id = RetroBoxFloppyId.Create();

        Assert.Equal(26, id.Length);
        Assert.True(RetroBoxCatalogRules.IsValidId(id));
        Assert.True(RetroBoxFloppyId.FitsNfcPayload(id, RetroBoxFloppyCatalogRules.ReadWriteMode));
    }

    [Fact]
    public void FitsNfcPayload_preserves_short_legacy_ids_and_rejects_oversized_ones()
    {
        Assert.True(RetroBoxFloppyId.FitsNfcPayload("monkey1-disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        Assert.False(RetroBoxFloppyId.FitsNfcPayload(
            "123456789012345678901234567890",
            RetroBoxFloppyCatalogRules.ReadOnlyMode));
    }
}
