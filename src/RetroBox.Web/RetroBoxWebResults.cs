using Microsoft.AspNetCore.Http;

namespace RetroBox.Web;

/// <summary>Result helpers shared by every endpoint group, so the AOT rule below is stated once.</summary>
public static class RetroBoxWebResults
{
    public static IResult Error(int statusCode, string code, string message)
    {
        // Pass the source-generated JsonTypeInfo directly: the JsonSerializerOptions overload of
        // Results.Json is annotated RequiresUnreferencedCode/RequiresDynamicCode because it cannot
        // statically prove the options carry a source-generated resolver, which is exactly the
        // AOT warning the RequestDelegateGenerator is meant to keep this project free of.
        return Results.Json(
            new RetroBoxErrorView(code, message),
            RetroBoxWebJsonContext.Default.RetroBoxErrorView,
            statusCode: statusCode);
    }
}
