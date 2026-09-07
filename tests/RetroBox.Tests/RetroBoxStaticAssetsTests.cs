using System.Text;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxStaticAssetsTests
{
    [Fact]
    public void TryGet_returns_the_generated_application_document()
    {
        Assert.True(RetroBoxStaticAssets.TryGet("index.html", out var content, out var contentType));

        var html = Encoding.UTF8.GetString(content);
        Assert.Equal("text/html; charset=utf-8", contentType);
        Assert.Contains("id=\"root\"", html, StringComparison.Ordinal);
        Assert.Contains("/assets/", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("")]
    [InlineData("missing.js")]
    public void TryGet_refuses_unknown_or_unsafe_assets(string relativePath)
    {
        Assert.False(RetroBoxStaticAssets.TryGet(relativePath, out _, out _));
    }

    [Fact]
    public void Generated_document_loads_no_external_network_asset()
    {
        Assert.True(RetroBoxStaticAssets.TryGet("index.html", out var content, out _));

        var html = Encoding.UTF8.GetString(content);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("//cdn.", html, StringComparison.OrdinalIgnoreCase);
    }
}
