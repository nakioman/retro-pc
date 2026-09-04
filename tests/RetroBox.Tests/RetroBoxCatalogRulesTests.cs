using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxCatalogRulesTests
{
    [Theory]
    [InlineData("MONKEY1.IMG", "monkey1")]
    [InlineData("Monkey Island Disk 1.ima", "monkey-island-disk-1")]
    [InlineData("mi_d1.img", "mi-d1")]
    [InlineData("sm91__d1.DSK", "sm91-d1")]
    [InlineData("--weird--.img", "weird")]
    public void Slugify_produces_a_valid_catalog_id(string fileName, string expected)
    {
        var slug = RetroBoxCatalogRules.Slugify(Path.GetFileNameWithoutExtension(fileName));

        Assert.Equal(expected, slug);
        Assert.True(RetroBoxCatalogRules.IsValidId(slug));
    }

    [Fact]
    public void Slugify_returns_an_empty_string_when_nothing_usable_remains()
    {
        Assert.Equal(string.Empty, RetroBoxCatalogRules.Slugify("!!!"));
    }
}
