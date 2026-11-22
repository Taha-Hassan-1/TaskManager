using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TeamBoard.API.Auth;

namespace TeamBoard.Tests;

public class AuthTests : IClassFixture<ApiFactory>
{
    private const string CookieName = "teamboard_refresh";
    private const string Password = "correct horse battery";
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public AuthTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateApiClient();
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];

    private async Task<(AuthResponse Body, string RefreshCookie, HttpResponseMessage Response)> RegisterAsync(string? name = null)
    {
        name ??= "u" + Unique();
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Username = name, Email = $"{name}@example.com", Password = Password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        return (body, CookieValue(response)!, response);
    }

    private static string? CookieValue(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        var header = values.FirstOrDefault(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal));
        return header?[(CookieName.Length + 1)..].Split(';')[0];
    }

    private Task<HttpResponseMessage> PostAuthAsync(string path, string? cookie, bool customHeader = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (cookie is not null) request.Headers.Add("Cookie", $"{CookieName}={cookie}");
        if (customHeader) request.Headers.Add("X-Requested-With", "TeamBoard");
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetMeAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    // ---- register ----

    [Fact]
    public async Task Register_ReturnsTokenAndSetsHardenedRefreshCookie()
    {
        var (body, cookie, response) = await RegisterAsync();

        Assert.False(string.IsNullOrEmpty(body.AccessToken));
        Assert.True(body.ExpiresAt > DateTime.UtcNow.AddMinutes(10) && body.ExpiresAt < DateTime.UtcNow.AddMinutes(16));

        var setCookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(CookieName + "="));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", setCookie, StringComparison.OrdinalIgnoreCase);

        // The raw token is never stored, only its hash.
        var hashes = await _factory.WithDbAsync(db => db.RefreshTokens.Select(t => t.TokenHash).ToListAsync());
        Assert.DoesNotContain(cookie, hashes);
        Assert.All(hashes, h => Assert.Equal(64, h.Length));
    }

    [Fact]
    public async Task Register_HashesPasswordWithBcryptWorkFactor12()
    {
        var (body, _, _) = await RegisterAsync();
        var hash = await _factory.WithDbAsync(db => db.Users.Where(u => u.Id == body.User.Id).Select(u => u.PasswordHash).SingleAsync());
        Assert.Matches(@"^\$2[aby]\$12\$", hash);
        Assert.DoesNotContain(Password, hash);
    }

    [Theory]
    [InlineData("ab", "x@example.com", "long enough password")]        // username too short
    [InlineData("valid_name", "not-an-email", "long enough password")] // bad email
    [InlineData("valid_name", "x@example.com", "short")]               // password under 10 characters
    [InlineData("bad name!", "x@example.com", "long enough password")] // username characters
    public async Task Register_InvalidInput_ReturnsValidationProblem(string username, string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Username = username, Email = email, Password = password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task Register_DuplicateEmailOrUsername_ReturnsConflict()
    {
        var name = "u" + Unique();
        await RegisterAsync(name);

        var sameEmail = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Username = "other" + Unique(), Email = $"{name.ToUpperInvariant()}@example.com", Password = Password });
        var sameName = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Username = name, Email = $"other{Unique()}@example.com", Password = Password });

        Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode); // email match is case-insensitive
        Assert.Equal(HttpStatusCode.Conflict, sameName.StatusCode);
    }

    // ---- login ----

    [Fact]
    public async Task Login_WithCorrectPassword_Succeeds()
    {
        var (registered, _, _) = await RegisterAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = registered.User.Email, Password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(CookieValue(response));
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_GiveTheSameAnswer()
    {
        var (registered, _, _) = await RegisterAsync();

        var wrongPassword = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = registered.User.Email, Password = "wrong password here" });
        var unknownEmail = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = "nobody@example.com", Password = "wrong password here" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        // Bodies match except for the per-request traceId.
        var a = (await wrongPassword.Content.ReadFromJsonAsync<ProblemDetails>())!;
        var b = (await unknownEmail.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal((a.Status, a.Title, a.Detail), (b.Status, b.Title, b.Detail));
        Assert.Null(CookieValue(wrongPassword));
    }

    // ---- access token validation ----

    [Fact]
    public async Task Me_RequiresAValidToken()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/me")).StatusCode);

        var (body, _, _) = await RegisterAsync();
        var ok = await GetMeAsync(body.AccessToken);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(body.User.Id, (await ok.Content.ReadFromJsonAsync<UserDto>())!.Id);
    }

    [Theory]
    [InlineData("TeamBoard", "TeamBoard.Web", ApiFactory.JwtKey, 5, true)]          // control: valid
    [InlineData("evil", "TeamBoard.Web", ApiFactory.JwtKey, 5, false)]              // wrong issuer
    [InlineData("TeamBoard", "someone-else", ApiFactory.JwtKey, 5, false)]          // wrong audience
    [InlineData("TeamBoard", "TeamBoard.Web", "another-key-0123456789-0123456789-0123456789", 5, false)] // wrong key
    [InlineData("TeamBoard", "TeamBoard.Web", ApiFactory.JwtKey, -5, false)]        // expired beyond the 1 minute skew
    public async Task AccessToken_IssuerAudienceSignatureAndLifetimeAreValidated(
        string issuer, string audience, string key, int minutesValid, bool accepted)
    {
        var (body, _, _) = await RegisterAsync();
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", body.User.Id.ToString())]),
            Issuer = issuer,
            Audience = audience,
            NotBefore = now.AddMinutes(-20),
            Expires = now.AddMinutes(minutesValid),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)
        };
        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        var response = await GetMeAsync(token);

        Assert.Equal(accepted ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- refresh ----

    [Fact]
    public async Task Refresh_RequiresTheCustomHeaderAndACookie()
    {
        var (_, cookie, _) = await RegisterAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAuthAsync("/api/auth/refresh", cookie, customHeader: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAuthAsync("/api/auth/refresh", cookie: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAuthAsync("/api/auth/refresh", "not-a-real-token")).StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesTheToken()
    {
        var (_, first, _) = await RegisterAsync();

        var response = await PostAuthAsync("/api/auth/refresh", first);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var second = CookieValue(response)!;
        Assert.NotEqual(first, second);
        var body = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(body.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAuthAsync("/api/auth/refresh", second)).StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusingARotatedToken_RevokesAllOfTheUsersTokens()
    {
        var (registered, stolen, _) = await RegisterAsync();
        var rotated = CookieValue(await PostAuthAsync("/api/auth/refresh", stolen))!; // the legitimate client rotates

        // The attacker replays the old token: refused, and the whole token family is killed.
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAuthAsync("/api/auth/refresh", stolen)).StatusCode);

        // So the legitimate client's newest token no longer works either.
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAuthAsync("/api/auth/refresh", rotated)).StatusCode);
        var active = await _factory.WithDbAsync(db =>
            db.RefreshTokens.CountAsync(t => t.UserId == registered.User.Id && t.RevokedAt == null));
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task Refresh_ReuseDoesNotAffectOtherUsers()
    {
        var (_, stolen, _) = await RegisterAsync();
        var (_, bystander, _) = await RegisterAsync();
        await PostAuthAsync("/api/auth/refresh", stolen);

        await PostAuthAsync("/api/auth/refresh", stolen); // replay

        Assert.Equal(HttpStatusCode.OK, (await PostAuthAsync("/api/auth/refresh", bystander)).StatusCode);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_IsRefused()
    {
        var (_, cookie, _) = await RegisterAsync();
        await _factory.WithDbAsync(async db =>
        {
            await db.RefreshTokens.ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            return 0;
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAuthAsync("/api/auth/refresh", cookie)).StatusCode);
    }

    [Fact]
    public async Task Refresh_ConcurrentUseOfOneToken_OnlyOneSucceeds()
    {
        var (_, cookie, _) = await RegisterAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostAuthAsync("/api/auth/refresh", cookie)));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    // ---- logout ----

    [Fact]
    public async Task Logout_RevokesTheTokenAndClearsTheCookie()
    {
        var (_, cookie, _) = await RegisterAsync();

        var logout = await PostAuthAsync("/api/auth/logout", cookie);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var cleared = logout.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(CookieName + "="));
        Assert.Contains("expires=", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAuthAsync("/api/auth/refresh", cookie)).StatusCode);
    }

    [Fact]
    public async Task Logout_RequiresTheCustomHeader()
    {
        var (_, cookie, _) = await RegisterAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAuthAsync("/api/auth/logout", cookie, customHeader: false)).StatusCode);
    }
}

// xUnit makes a new instance of this class for every test, so each test gets its own app and its own rate limiter.
// (Sharing one through IClassFixture made the result depend on which test ran first.)
public sealed class AuthRateLimitTests : IAsyncLifetime
{
    private readonly RateLimitedApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await ((IAsyncLifetime)_factory).InitializeAsync(); // creates and migrates this test's own database
        _client = _factory.CreateApiClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await ((IAsyncLifetime)_factory).DisposeAsync();
    }

    [Fact]
    public async Task Login_IsRateLimitedPerIp()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest { Email = "nobody@example.com", Password = "wrong password here" });
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                Assert.True(response.Headers.Contains("Retry-After"));
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task EachEndpointHasItsOwnLimit()
    {
        for (var i = 0; i < 4; i++)
            await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = "a@example.com", Password = "x" });

        var register = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Username = "ratelimit_user", Email = "rl@example.com", Password = "long enough password" });

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
    }
}
