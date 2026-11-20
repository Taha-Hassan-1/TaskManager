using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskManager.API.Data;

namespace TaskManager.API.Auth;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting(AuthServiceCollectionExtensions.RateLimitPolicy)]
public sealed class AuthController(AuthService auth, IOptions<RefreshOptions> refreshOptions) : ControllerBase
{
    private const string CookiePath = "/api/auth";

    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var outcome = await auth.RegisterAsync(request, ct);
        return outcome.Error switch
        {
            AuthError.None => Signed(outcome.Session!, StatusCodes.Status201Created),
            AuthError.EmailTaken => Problem(statusCode: StatusCodes.Status409Conflict, title: "Email is already registered."),
            AuthError.UsernameTaken => Problem(statusCode: StatusCodes.Status409Conflict, title: "Username is already taken."),
            _ => Problem(statusCode: StatusCodes.Status400BadRequest)
        };
    }

    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var outcome = await auth.LoginAsync(request, ct);
        // Same answer for an unknown email and a wrong password.
        return outcome.Session is null
            ? Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.")
            : Signed(outcome.Session, StatusCodes.Status200OK);
    }

    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (!HasCustomHeader()) return MissingHeader();

        var outcome = await auth.RefreshAsync(Request.Cookies[refreshOptions.Value.CookieName], ct);
        if (outcome.Session is null)
        {
            ClearRefreshCookie();
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Refresh token is not valid.");
        }
        return Signed(outcome.Session, StatusCodes.Status200OK);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!HasCustomHeader()) return MissingHeader();

        await auth.LogoutAsync(Request.Cookies[refreshOptions.Value.CookieName], ct);
        ClearRefreshCookie();
        return NoContent();
    }

    // A cross-site form post cannot add a custom header, so requiring one on the cookie-authenticated
    // endpoints blocks CSRF on top of SameSite=Strict.
    private bool HasCustomHeader() => Request.Headers.ContainsKey("X-Requested-With");

    private ObjectResult MissingHeader() =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "Missing X-Requested-With header.");

    private ObjectResult Signed(AuthSession session, int statusCode)
    {
        Response.Cookies.Append(refreshOptions.Value.CookieName, session.RefreshRaw, CookieOptions(session.RefreshExpiresAt));
        var user = new UserDto(session.User.Id, session.User.Username, session.User.Email);
        return StatusCode(statusCode, new AuthResponse(session.Access.Value, session.Access.ExpiresAt, user));
    }

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(refreshOptions.Value.CookieName, CookieOptions(null));

    // Scoped to /api/auth so the browser sends it nowhere else; JavaScript can never read it.
    private static CookieOptions CookieOptions(DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        IsEssential = true,
        Expires = expires is null ? null : new DateTimeOffset(DateTime.SpecifyKind(expires.Value, DateTimeKind.Utc))
    };
}

[ApiController]
[Route("api/me")]
[Authorize]
public sealed class MeController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!int.TryParse(base.User.FindFirst("sub")?.Value, out var id)) return Unauthorized();

        var me = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserDto(u.Id, u.Username, u.Email))
            .SingleOrDefaultAsync(ct);
        return me is null ? Unauthorized() : Ok(me);
    }
}
