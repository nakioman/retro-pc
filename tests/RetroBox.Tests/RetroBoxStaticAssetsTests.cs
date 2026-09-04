using System.Text;
using System.Text.RegularExpressions;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxStaticAssetsTests
{
    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("app.css", "text/css; charset=utf-8")]
    [InlineData("app.js", "text/javascript; charset=utf-8")]
    public void TryGet_returns_each_embedded_asset(string relativePath, string expectedContentType)
    {
        Assert.True(RetroBoxStaticAssets.TryGet(relativePath, out var content, out var contentType));
        Assert.NotEmpty(content);
        Assert.Equal(expectedContentType, contentType);
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("nope.js")]
    [InlineData("")]
    public void TryGet_refuses_anything_that_is_not_an_asset(string relativePath)
    {
        Assert.False(RetroBoxStaticAssets.TryGet(relativePath, out _, out _));
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("app.css")]
    [InlineData("app.js")]
    public void The_panel_loads_nothing_from_the_network(string relativePath)
    {
        Assert.True(RetroBoxStaticAssets.TryGet(relativePath, out var content, out _));

        var text = Encoding.UTF8.GetString(content);

        Assert.DoesNotContain("http://", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("//cdn.", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_no_NFC_badge_carries_its_explanation()
    {
        // An uploaded floppy always lands with nfc: false and cannot be inserted until a tag is
        // written for it, which the panel cannot do yet. The badge is the only place the panel
        // says so, so both the help string and the attribute that surfaces it are pinned here.
        Assert.True(RetroBoxStaticAssets.TryGet("app.js", out var js, out _));

        var script = Encoding.UTF8.GetString(js);

        Assert.Contains("untaggedHelp", script, StringComparison.Ordinal);
        Assert.Contains("badge.title = t(\"untaggedHelp\")", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_languages_define_exactly_the_same_keys()
    {
        Assert.True(RetroBoxStaticAssets.TryGet("app.js", out var js, out _));

        var script = Encoding.UTF8.GetString(js);
        var spanish = ExtractKeys(script, "es");
        var english = ExtractKeys(script, "en");

        Assert.NotEmpty(spanish);
        Assert.Equal(spanish, english);
    }

    [Theory]
    [InlineData("no-tag-present")]
    [InlineData("tag-already-assigned")]
    [InlineData("write-failed")]
    [InlineData("write-unconfirmed")]
    [InlineData("no-controller")]
    public void Every_nfc_error_code_has_text_in_both_languages(string code)
    {
        Assert.True(RetroBoxStaticAssets.TryGet("app.js", out var js, out _));

        var script = Encoding.UTF8.GetString(js);
        var occurrences = Regex.Matches(script, $"\"{code}\"\\s*:").Count;

        Assert.Equal(2, occurrences);
    }

    private static string[] ExtractKeys(string script, string language)
    {
        var block = Regex.Match(
            script,
            $@"{language}:\s*\{{(?<body>.*?)\n  \}}",
            RegexOptions.Singleline);

        Assert.True(block.Success, $"Could not find the '{language}' dictionary in app.js.");

        return Regex.Matches(block.Groups["body"].Value, @"^\s{4}""?([A-Za-z0-9_-]+)""?:", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }
}
