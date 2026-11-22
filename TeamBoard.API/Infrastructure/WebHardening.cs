using System.Text.RegularExpressions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.FileProviders;
using Serilog.Context;

namespace TeamBoard.API.Infrastructure;

/// <summary>
/// Everything between "the app works" and "the app can face the internet": one correlation id per request,
/// security headers, compression, caching rules for the Angular files, and the proxy and HTTPS switches.
/// </summary>
public static class WebHardening
{
    private static readonly string[] ApiPrefixes = ["/api", "/hubs", "/health", "/openapi", "/scalar"];

    public const string CorrelationHeader = "X-Correlation-Id";
    private const string CorrelationItem = "CorrelationId";

    public static IServiceCollection AddWebHardening(this IServiceCollection services, IConfiguration config)
    {
        services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true; // JSON that carries no secret next to attacker-controlled text; see docs/ARCHITECTURE.md
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
        });

        // Opt-in: behind a reverse proxy the client's address and scheme arrive in X-Forwarded-* headers. Trusting
        // them when the app is reachable directly would let anyone pick the address the rate limiter sees.
        if (config.GetValue<bool>("ForwardedHeaders:Enabled"))
        {
            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownIPNetworks.Clear(); // the platform's proxy is not on a fixed address
                o.KnownProxies.Clear();
            });
        }

        // Behind a proxy that ends TLS the app cannot see the public port, and without one it does not redirect at all.
        services.AddHttpsRedirection(o => o.HttpsPort = config.GetValue("Https:Port", 443));

        services.AddHsts(o =>
        {
            o.MaxAge = TimeSpan.FromDays(365); // browsers then refuse plain http for this site for a year
            o.IncludeSubDomains = true;
        });

        services.AddProblemDetails(o => o.CustomizeProblemDetails = context =>
        {
            // The same id is in the response header and in every log line of the request, so "it failed" can be traced.
            context.ProblemDetails.Extensions["correlationId"] = CorrelationIdOf(context.HttpContext);
        });

        return services;
    }

    public static string CorrelationIdOf(HttpContext context) =>
        context.Items[CorrelationItem] as string ?? context.TraceIdentifier;

    /// <summary>The first half of the pipeline: everything that must run before routing and authentication.</summary>
    public static WebApplication UseWebHardeningFirst(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled")) app.UseForwardedHeaders();

        app.Use(CorrelationId);
        app.UseExceptionHandler(); // unhandled exception -> ProblemDetails, cause logged, nothing leaked

        if (!app.Environment.IsDevelopment()) app.UseHsts();
        if (app.Configuration.GetValue("Https:Redirect", !app.Environment.IsDevelopment())) app.UseHttpsRedirection();

        app.Use(SecurityHeaders(app.Environment.IsDevelopment()));
        app.UseResponseCompression();
        return app;
    }

    // ---- correlation id ----

    private static async Task CorrelationId(HttpContext context, Func<Task> next)
    {
        var supplied = context.Request.Headers[CorrelationHeader].FirstOrDefault();
        // Accept a caller's id only if it is harmless to put in a log line and a header.
        var id = supplied is { Length: > 0 and <= 64 } && Regex.IsMatch(supplied, "^[A-Za-z0-9._-]+$") ? supplied : Guid.NewGuid().ToString("N");

        context.Items[CorrelationItem] = id;
        // When the headers are sent, not now: the exception handler clears the response, and the id must survive that.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeader] = id;
            return Task.CompletedTask;
        });
        using (LogContext.PushProperty("CorrelationId", id)) await next();
    }

    // ---- security headers ----

    // The Angular app needs inline styles (Material), fonts from Google Fonts, and nothing else from outside.
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com data:; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private static Func<HttpContext, Func<Task>, Task> SecurityHeaders(bool development) => async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";

            // The API explorer in development loads scripts from a CDN, so the strict policy is for everything else.
            if (!development) h["Content-Security-Policy"] = ContentSecurityPolicy;

            // Account and board data must never sit in a shared cache.
            if (context.Request.Path.StartsWithSegments("/api")) h.CacheControl = "no-store";
            return Task.CompletedTask;
        });
        await next();
    };

    // ---- the Angular app, served by the API ----

    /// <summary>The folder the built Angular app is served from, or null when there is none (API only).</summary>
    private static PhysicalFileProvider? SpaFiles(WebApplication app)
    {
        var root = app.Configuration["Spa:Root"] is { Length: > 0 } configured
            ? Path.GetFullPath(configured, app.Environment.ContentRootPath)
            : Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        return Directory.Exists(root) ? new PhysicalFileProvider(root) : null;
    }

    /// <summary>
    /// Serves the built Angular app's files (from <c>Spa:Root</c>, default wwwroot) when they exist: long caching for the
    /// hashed bundles and none for index.html. Before routing, so the files are served before any endpoint is matched.
    /// </summary>
    public static WebApplication UseSpaFiles(this WebApplication app)
    {
        if (SpaFiles(app) is not { } files) return app;

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = context =>
            {
                var name = context.File.Name;
                // Angular names bundles main-ABC12345.js: the content is in the name, so they can be cached for good.
                var hashed = Regex.IsMatch(name, @"-[A-Za-z0-9]{8}\.(js|css)$");
                context.Context.Response.Headers.CacheControl = hashed
                    ? "public, max-age=31536000, immutable"
                    : "no-cache"; // index.html and the rest: always check, so a new release shows up at once
            }
        });
        return app;
    }

    /// <summary>
    /// Client-side routes (/boards/3) come back as the app, so a refresh or a shared link works. Mapped after every
    /// real endpoint; unknown API, hub and health paths stay 404s, not HTML.
    /// </summary>
    public static WebApplication MapSpaFallback(this WebApplication app)
    {
        if (SpaFiles(app) is not { } files) return app;

        // Not MapFallbackToFile with a regex that excludes /api: that pattern made real controller routes 404.
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            if (ApiPrefixes.Any(prefix => path.StartsWithSegments(prefix)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound; // the status-code middleware gives it a ProblemDetails body
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.SendFileAsync(files.GetFileInfo("index.html"));
        });
        return app;
    }
}
