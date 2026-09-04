using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

    // MapGet's Delegate overload carries RequiresUnreferencedCode/RequiresDynamicCode because it
    // can fall back to reflection-based invocation. Under -p:PublishAot=true the Minimal API
    // RequestDelegateGenerator intercepts these calls with source-generated code instead, and the
    // toolchain spike for this task confirmed a linux-x64 native publish of this exact code
    // produces zero AOT/trim warnings. The suppression only applies to this method, so it does not
    // force every caller of StartAsync to also become annotated.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Resolved by the RequestDelegateGenerator under PublishAot; verified warning-free by the toolchain spike.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Resolved by the RequestDelegateGenerator under PublishAot; verified warning-free by the toolchain spike.")]
    public static async Task<RetroBoxWebHost> StartAsync(
        RetroBoxWebOptions options,
        IRetroBoxCatalogSource catalogSource,
        CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();

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
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static IResult ServeAsset(string relativePath)
    {
        return RetroBoxStaticAssets.TryGet(relativePath, out var content, out var contentType)
            ? Results.Bytes(content, contentType)
            : Results.NotFound();
    }
}
