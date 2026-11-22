using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TeamBoard.API.Models;

namespace TeamBoard.API.Auth;

public sealed record AccessToken(string Value, DateTime ExpiresAt);

/// <param name="Raw">Sent to the client once, in a cookie. Never stored.</param>
/// <param name="Hash">SHA-256 hex of <paramref name="Raw"/>; the only form kept in the database.</param>
public sealed record NewRefreshToken(string Raw, string Hash);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
    NewRefreshToken CreateRefreshToken();
    string Hash(string rawToken);
}

public sealed class TokenService(IOptions<JwtOptions> jwt, TimeProvider time) : ITokenService
{
    private readonly JwtOptions _jwt = jwt.Value;

    public AccessToken CreateAccessToken(User user)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim("name", user.Username),
                new Claim(JwtRegisteredClaimNames.Email, user.Email)
            ]),
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            NotBefore = now,
            IssuedAt = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)), SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public NewRefreshToken CreateRefreshToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return new NewRefreshToken(raw, Hash(raw));
    }

    public string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
