using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TeamBoard.API.Data;
using TeamBoard.API.Models;

namespace TeamBoard.API.Auth;

public enum AuthError { None, InvalidCredentials, EmailTaken, UsernameTaken, InvalidRefreshToken }

public sealed record AuthSession(User User, AccessToken Access, string RefreshRaw, DateTime RefreshExpiresAt);

public sealed record AuthOutcome(AuthSession? Session, AuthError Error)
{
    public static AuthOutcome Ok(AuthSession session) => new(session, AuthError.None);
    public static AuthOutcome Fail(AuthError error) => new(null, error);
}

public sealed class AuthService(
    AppDbContext db, ITokenService tokens, IOptions<RefreshOptions> refresh, TimeProvider time)
{
    private const int BcryptWorkFactor = 12;

    // Verified against when the email is unknown, so a miss costs the same as a wrong password.
    private static readonly Lazy<string> DummyHash =
        new(() => BCrypt.Net.BCrypt.HashPassword("not-a-real-password", BcryptWorkFactor));

    public async Task<AuthOutcome> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var username = request.Username.Trim();

        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return AuthOutcome.Fail(AuthError.EmailTaken);
        if (await db.Users.AnyAsync(u => u.Username == username, ct)) return AuthOutcome.Fail(AuthError.UsernameTaken);

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BcryptWorkFactor),
            CreatedAt = time.GetUtcNow().UtcDateTime
        };
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Lost a race with a concurrent registration; the unique indexes are the real guard.
            return AuthOutcome.Fail(AuthError.EmailTaken);
        }

        return AuthOutcome.Ok(await IssueSessionAsync(user, ct));
    }

    public async Task<AuthOutcome> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        var hash = user?.PasswordHash ?? DummyHash.Value;
        var valid = BCrypt.Net.BCrypt.Verify(request.Password, hash);
        if (user is null || !valid) return AuthOutcome.Fail(AuthError.InvalidCredentials);

        return AuthOutcome.Ok(await IssueSessionAsync(user, ct));
    }

    public async Task<AuthOutcome> RefreshAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(rawToken)) return AuthOutcome.Fail(AuthError.InvalidRefreshToken);

        var hash = tokens.Hash(rawToken);
        var stored = await db.RefreshTokens.Include(t => t.User).SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return AuthOutcome.Fail(AuthError.InvalidRefreshToken);

        var now = time.GetUtcNow().UtcDateTime;

        // A token that was already rotated is back: it was copied or stolen. Revoke everything the user has.
        // (A token revoked by logout has no replacement, so replaying it is just refused.)
        if (stored.RevokedAt is not null)
        {
            if (stored.ReplacedByTokenId is not null) await RevokeAllAsync(stored.UserId, now, ct);
            return AuthOutcome.Fail(AuthError.InvalidRefreshToken);
        }

        if (stored.ExpiresAt <= now) return AuthOutcome.Fail(AuthError.InvalidRefreshToken);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Atomic claim: of two concurrent refreshes with the same token only one updates a row.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            await RevokeAllAsync(stored.UserId, now, ct);
            return AuthOutcome.Fail(AuthError.InvalidRefreshToken);
        }

        var (session, replacement) = await IssueSessionCoreAsync(stored.User, ct);
        await db.RefreshTokens
            .Where(t => t.Id == stored.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedByTokenId, replacement.Id), ct);

        await tx.CommitAsync(ct);
        return AuthOutcome.Ok(session);
    }

    public async Task LogoutAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(rawToken)) return;

        var hash = tokens.Hash(rawToken);
        var now = time.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private async Task<AuthSession> IssueSessionAsync(User user, CancellationToken ct) =>
        (await IssueSessionCoreAsync(user, ct)).Session;

    private async Task<(AuthSession Session, RefreshToken Stored)> IssueSessionCoreAsync(User user, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var newToken = tokens.CreateRefreshToken();
        var stored = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newToken.Hash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(refresh.Value.Days)
        };
        db.RefreshTokens.Add(stored);
        await db.SaveChangesAsync(ct);

        return (new AuthSession(user, tokens.CreateAccessToken(user), newToken.Raw, stored.ExpiresAt), stored);
    }

    private Task<int> RevokeAllAsync(int userId, DateTime now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
}
