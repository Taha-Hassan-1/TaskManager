using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.API.Auth;
using TaskManager.API.Models;

namespace TaskManager.Tests;

/// <summary>The app as it runs for real: Production settings, so the strict policies are on.</summary>
public sealed class ProductionApiFactory : ApiFactory
{
    protected override string EnvironmentName => "Production";
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string> { ["Https:Redirect"] = "false" };
}

/// <summary>Production with the HTTPS redirect left at its default (on), and the proxy headers trusted.</summary>
public sealed class BehindProxyApiFactory : ApiFactory
{
    protected override string EnvironmentName => "Production";
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string> { ["ForwardedHeaders:Enabled"] = "true" };
}

/// <summary>Production, redirect on, and no trust in proxy headers (the default).</summary>
public sealed class DirectApiFactory : ApiFactory
{
    protected override string EnvironmentName => "Production";
}

public class SecurityHeaderTests : IClassFixture<ProductionApiFactory>, IClassFixture<ApiFactory>
{
    private readonly ProductionApiFactory _production;
    private readonly ApiFactory _development;

    public SecurityHeaderTests(ProductionApiFactory production, ApiFactory development)
    {
        _production = production;
        _development = development;
    }

    [Fact]
    public async Task EveryResponse_CarriesTheSecurityHeaders()
    {
        var response = await _production.CreateApiClient().GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString()); // account data never sits in a shared cache
    }

    [Fact]
    public async Task TheContentSecurityPolicy_AllowsOnlyTheAppItself()
    {
        var response = await _production.CreateApiClient().GetAsync("/api/me");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self';", csp); // no inline scripts, no eval, no other origins
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.DoesNotMatch(@"script-src[^;]*unsafe-inline", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'self'", csp);
        Assert.Contains("connect-src 'self'", csp); // API and live updates on the same origin, nothing else
    }

    [Fact]
    public async Task InDevelopment_TheApiExplorerIsNotBlockedByThePolicy()
    {
        var response = await _development.CreateApiClient().GetAsync("/api/me");
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.True(response.Headers.Contains("X-Content-Type-Options")); // the rest still applies
    }

    [Fact]
    public async Task Responses_AreCompressedWhenTheClientAsksForIt()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        request.Headers.AcceptEncoding.ParseAdd("br, gzip");

        var response = await _development.CreateApiClient().SendAsync(request);

        Assert.Contains(response.Content.Headers.ContentEncoding, e => e is "br" or "gzip");
    }
}

public class HttpsTests : IClassFixture<DirectApiFactory>, IClassFixture<BehindProxyApiFactory>
{
    private readonly DirectApiFactory _direct;
    private readonly BehindProxyApiFactory _proxied;

    public HttpsTests(DirectApiFactory direct, BehindProxyApiFactory proxied)
    {
        _direct = direct;
        _proxied = proxied;
    }

    private static HttpClient Client(ApiFactory factory, string baseAddress) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(baseAddress), AllowAutoRedirect = false, HandleCookies = false,
    });

    [Fact]
    public async Task PlainHttp_IsRedirectedToHttps()
    {
        var response = await Client(_direct, "http://teamboard.example").GetAsync("/boards");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.StartsWith("https://teamboard.example/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Https_GetsAStrictTransportSecurityHeader()
    {
        var response = await Client(_direct, "https://teamboard.example").GetAsync("/api/me");

        var hsts = response.Headers.GetValues("Strict-Transport-Security").Single();
        Assert.Matches(@"max-age=(\d+)", hsts);
        Assert.True(long.Parse(System.Text.RegularExpressions.Regex.Match(hsts, @"max-age=(\d+)").Groups[1].Value) >= 31_536_000);
    }

    [Fact]
    public async Task ProxyHeaders_AreIgnoredUnlessTheDeploymentSaysToTrustThem()
    {
        // Reachable directly: anyone could send this header, so it must not turn plain http into "already https".
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await Client(_direct, "http://teamboard.example").SendAsync(request);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
    }

    [Fact]
    public async Task BehindATrustedProxy_TheForwardedSchemeIsHonoured()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Forwarded-Proto", "https"); // the proxy ended TLS and says so

        var response = await Client(_proxied, "http://teamboard.example").SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // reached the app, no redirect loop
    }
}

public class CorrelationIdTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public CorrelationIdTests(ApiFactory factory) => _client = factory.CreateApiClient();

    [Fact]
    public async Task EveryResponse_HasACorrelationId()
    {
        var response = await _client.GetAsync("/api/me");
        var id = response.Headers.GetValues("X-Correlation-Id").Single();
        Assert.Matches("^[0-9a-f]{32}$", id);
    }

    [Fact]
    public async Task ACallersIdIsKept_SoARequestCanBeFollowedAcrossSystems()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Correlation-Id", "web-7f3a.001");

        var response = await _client.SendAsync(request);

        Assert.Equal("web-7f3a.001", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("line\\nbreak")]
    public async Task AnIdThatIsNotSafeToLog_IsReplaced(string supplied)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", supplied);

        var response = await _client.SendAsync(request);

        Assert.Matches("^[0-9a-f]{32}$", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task ErrorBodies_CarryTheSameId()
    {
        var response = await _client.GetAsync("/api/me");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), problem!.Extensions["correlationId"]!.ToString());
    }
}

public class HealthTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public HealthTests(ApiFactory factory) => _client = factory.CreateApiClient();

    [Fact]
    public async Task Live_ItIsUp() => Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/live")).StatusCode);

    [Fact]
    public async Task Ready_ItCanReachItsDatabase() => Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/ready")).StatusCode);
}

/// <summary>The app with a database that cannot be reached.</summary>
public sealed class BrokenDatabaseTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Production")
        .UseSetting("Https:Redirect", "false")
        .UseSetting("Serilog:MinimumLevel:Default", "Fatal")
        .UseSetting("Jwt:Key", ApiFactory.JwtKey)
        .UseSetting("ConnectionStrings:DefaultConnection", "Server=127.0.0.1,1;Database=none;User Id=x;Password=y;Connect Timeout=1;ConnectRetryCount=0;TrustServerCertificate=True"));

    public void Dispose() => _factory.Dispose();

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task Ready_ReportsUnavailable_WhileLiveStaysUp()
    {
        Assert.Equal(HttpStatusCode.OK, (await Client().GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client().GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task AnUnhandledFailure_IsAProblemWithACorrelationId_AndLeaksNothing()
    {
        var token = _factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(new User { Id = 1, Username = "x", Email = "x@example.com" }).Value;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/boards");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(response.Headers.GetValues("X-Correlation-Id").Single(), body);
        Assert.DoesNotContain("SqlException", body);
        Assert.DoesNotContain("127.0.0.1", body);   // not the server address
        Assert.DoesNotContain(" at ", body);         // not a stack trace
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}

/// <summary>The built Angular app, served by the API.</summary>
public sealed class SpaServingTests : IClassFixture<SpaServingTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        public string Folder { get; } = Directory.CreateTempSubdirectory("teamboard-spa-").FullName;

        public Factory()
        {
            File.WriteAllText(Path.Combine(Folder, "index.html"), "<!doctype html><title>TeamBoard</title><app-root></app-root>");
            File.WriteAllText(Path.Combine(Folder, "main-ABCD1234.js"), "console.log('app');");
            File.WriteAllText(Path.Combine(Folder, "favicon.ico"), "icon");
        }

        protected override string EnvironmentName => "Production";
        protected override IReadOnlyDictionary<string, string> ExtraSettings =>
            new Dictionary<string, string> { ["Spa:Root"] = Folder, ["Https:Redirect"] = "false" };

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
    }

    private readonly HttpClient _client;

    public SpaServingTests(Factory factory) => _client = factory.CreateApiClient();

    [Fact]
    public async Task TheRoot_IsTheApp_AndIsNeverCached()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<app-root>", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString()); // a new release shows up at once
    }

    [Fact]
    public async Task HashedBundles_AreCachedForGood()
    {
        var response = await _client.GetAsync("/main-ABCD1234.js");

        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task OtherFiles_AreRevalidated()
    {
        var response = await _client.GetAsync("/favicon.ico");
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/boards")]
    [InlineData("/boards/3/cards/9")]
    [InlineData("/login")]
    public async Task ClientSideRoutes_ComeBackAsTheApp_SoRefreshAndSharedLinksWork(string route)
    {
        var response = await _client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<app-root>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RealApiRoutes_StillWork_WhileTheAppIsServed()
    {
        // The SPA fallback must never swallow a route that exists.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/boards")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/live")).StatusCode);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode); // reached the controller, not index.html
    }

    [Theory]
    [InlineData("/api/no-such-thing")]
    [InlineData("/hubs/no-such-hub")]
    public async Task UnknownApiPaths_StayNotFound_InsteadOfReturningHtml(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("<app-root>", await response.Content.ReadAsStringAsync());
    }
}
