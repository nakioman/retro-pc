using System.Net;
using System.Text;
using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxScreenScraperCoverSourceTests
{
    [Fact]
    public async Task SearchAsync_requires_all_credentials_before_making_a_request()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("A request must not be sent."));
        using var client = new HttpClient(handler);
        var source = new RetroBoxScreenScraperCoverSource(client, new RetroBoxScraperSettings { DevId = "developer" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => source.SearchAsync("Doom", CancellationToken.None));

        Assert.Equal("ScreenScraper credentials are not configured.", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_sends_the_pc_dos_system_id_and_filters_results_without_a_usable_box_2d()
    {
        Uri? requestUri = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return JsonResponse("""
                {"response":{"jeux":[
                  {"id":"1","noms":[{"text":"Doom"}],"medias":[{"type":"box-2D","url":"https://covers.example/doom.png"}]},
                  {"id":"2","noms":[{"text":"No cover"}],"medias":[{"type":"video","url":"https://covers.example/video.mp4"}]},
                  {"id":"3","noms":[{"text":"Empty cover"}],"medias":[{"type":"box-2D","url":""}]}
                ]}}
                """);
        });
        using var client = new HttpClient(handler);
        var source = new RetroBoxScreenScraperCoverSource(client, ConfiguredSettings());

        var results = await source.SearchAsync("Doom II", CancellationToken.None);

        Assert.Equal("/api2/jeuRecherche.php", requestUri!.AbsolutePath);
        Assert.Contains("systemeid=135", requestUri.Query, StringComparison.Ordinal);
        Assert.Contains("recherche=Doom%20II", requestUri.Query, StringComparison.Ordinal);
        var result = Assert.Single(results);
        Assert.Equal("1", result.ScreenScraperId);
        Assert.Equal("Doom", result.Title);
    }

    [Fact]
    public async Task GetGameAsync_propagates_a_screen_scraper_error_without_credential_values()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {"header":{"success":"false","error":"Daily quota exhausted"}}
            """));
        using var client = new HttpClient(handler);
        var source = new RetroBoxScreenScraperCoverSource(client, ConfiguredSettings());

        var exception = await Assert.ThrowsAsync<RetroBoxScreenScraperException>(() => source.GetGameAsync("42", CancellationToken.None));

        Assert.Equal("Daily quota exhausted", exception.Message);
        Assert.DoesNotContain("developer-password", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("user-password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_preserves_caller_cancellation()
    {
        var handler = new StubHttpMessageHandler(_ => throw new OperationCanceledException());
        using var client = new HttpClient(handler);
        var source = new RetroBoxScreenScraperCoverSource(client, ConfiguredSettings());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.SearchAsync("Doom", cancellation.Token));
    }

    [Fact]
    public void Select_prefers_region_then_language_priority()
    {
        var selector = new RetroBoxCoverSelector();
        var candidates = new[]
        {
            new RetroBoxCoverMedia("box-2D", "https://covers.example/us-es.png", "us", "es"),
            new RetroBoxCoverMedia("box-2D", "https://covers.example/sp-en.png", "sp", "en"),
            new RetroBoxCoverMedia("box-2D", "https://covers.example/sp-es.png", "sp", "es"),
        };

        var selected = selector.Select(candidates, ["sp", "us"], ["es", "en"]);

        Assert.Equal("https://covers.example/sp-es.png", selected!.Url);
    }

    [Fact]
    public void Select_prefers_a_higher_priority_region_when_its_cover_has_no_language()
    {
        var selector = new RetroBoxCoverSelector();
        var candidates = new[]
        {
            new RetroBoxCoverMedia("box-2D", "https://covers.example/us-en.png", "us", "en"),
            new RetroBoxCoverMedia("box-2D", "https://covers.example/sp-unlabeled.png", "sp", null),
        };

        var selected = selector.Select(candidates, ["sp", "us"], ["es", "en"]);

        Assert.Equal("https://covers.example/sp-unlabeled.png", selected!.Url);
    }

    [Fact]
    public void Select_returns_the_first_usable_box_2d_when_no_priority_matches()
    {
        var selector = new RetroBoxCoverSelector();
        var candidates = new[]
        {
            new RetroBoxCoverMedia("box-2D", "https://covers.example/first.png", "jp", "ja"),
            new RetroBoxCoverMedia("box-2D", "https://covers.example/second.png", "fr", "fr"),
        };

        var selected = selector.Select(candidates, ["sp"], ["es"]);

        Assert.Equal("https://covers.example/first.png", selected!.Url);
    }

    private static RetroBoxScraperSettings ConfiguredSettings() => new()
    {
        DevId = "developer",
        DevPassword = "developer-password",
        SsId = "user",
        SsPassword = "user-password",
    };

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }
}
