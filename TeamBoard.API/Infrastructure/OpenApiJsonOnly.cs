using Microsoft.AspNetCore.OpenApi;

namespace TeamBoard.API.Infrastructure;

public static class OpenApiJsonOnly
{
    private static readonly string[] Extra = ["application/*+json", "text/json", "text/plain"];

    /// <summary>
    /// ASP.NET advertises several JSON aliases per request body and response. They are all the same
    /// thing, and generated clients would pick one of them as the Content-Type header, so keep just application/json.
    /// </summary>
    public static OpenApiOptions UseJsonOnly(this OpenApiOptions options) =>
        options.AddDocumentTransformer((document, _, _) =>
        {
            foreach (var path in document.Paths.Values)
            {
                foreach (var operation in (IEnumerable<Microsoft.OpenApi.OpenApiOperation>?)path.Operations?.Values ?? [])
                {
                    var content = operation.RequestBody?.Content;
                    if (content is not null)
                        foreach (var alias in Extra) content.Remove(alias);
                }
            }
            return Task.CompletedTask;
        });
}
