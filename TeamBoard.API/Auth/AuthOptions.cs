using System.ComponentModel.DataAnnotations;

namespace TeamBoard.API.Auth;

// Validated at startup (ValidateOnStart): bad configuration fails at boot, not on the first login.
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    // 32 characters is at least 32 bytes in UTF-8. Comes from user-secrets or the environment, never the repo.
    [Required, MinLength(32)] public string Key { get; set; } = "";
    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";
    [Range(1, 60)] public int AccessTokenMinutes { get; set; } = 15;
}

public sealed class RefreshOptions
{
    public const string Section = "Refresh";

    [Range(1, 30)] public int Days { get; set; } = 7;
    [Required] public string CookieName { get; set; } = "teamboard_refresh";
}

public sealed class RateLimitOptions
{
    public const string Section = "RateLimit";

    [Range(1, 100_000)] public int AuthPermitPerMinute { get; set; } = 10;
}
