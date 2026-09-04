using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RetroBox.Core;

namespace RetroBox.Web;

public sealed class RetroBoxWebHost : IAsyncDisposable
{
    private readonly WebApplication app;

    private RetroBoxWebHost(WebApplication app, Uri baseAddress)
    {
        this.app = app;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public static async Task<RetroBoxWebHost> StartAsync(
        RetroBoxWebOptions options,
        IRetroBoxCatalogSource catalogSource,
        CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Kestrel and the generic host log to Console.Out by default, the same writer the daemon
        // uses for its own operator output (floppy-insert lines, catalog diagnostics). Left alone
        // every request prints four INFO lines and buries the daemon's own log; the startup and
        // shutdown status banners are misleading too, since the CLI's own Console.CancelKeyPress
        // handler drives shutdown, not this host's lifetime messages.
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.Configure<ConsoleLifetimeOptions>(lifetime => lifetime.SuppressStatusMessages = true);

        // Bound to every interface on purpose: the panel is useless if it is not reachable from
        // a phone on the LAN.
        builder.WebHost.UseUrls($"http://0.0.0.0:{options.Port}");
        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, RetroBoxWebJsonContext.Default));

        var app = builder.Build();

        app.MapGet("/api/catalog", () => RetroBoxCatalogEndpoints.BuildCatalogView(catalogSource));
        app.MapGet("/", () => ServeAsset("index.html"));
        app.MapGet("/{asset}", (string asset) => ServeAsset(asset));

        await app.StartAsync(cancellationToken);

        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel reported no bound address.");

        // Kestrel reports 0.0.0.0; a client has to dial a routable host.
        return new RetroBoxWebHost(app, new Uri(address.Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal)));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await app.StopAsync();
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static IResult ServeAsset(string relativePath)
    {
        return RetroBoxStaticAssets.TryGet(relativePath, out var content, out var contentType)
            ? Results.Bytes(content, contentType)
            : Results.NotFound();
    }
}
