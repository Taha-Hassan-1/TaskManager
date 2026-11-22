using System.ComponentModel.DataAnnotations;

namespace TeamBoard.API.Auth;

public sealed class RegisterRequest
{
    [Required, StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9_.-]+$", ErrorMessage = "Username may contain letters, digits, '_', '.' and '-'.")]
    public string Username { get; set; } = "";

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    // BCrypt only uses the first 72 bytes, so longer passwords would be silently truncated.
    [Required, StringLength(72, MinimumLength = 10)]
    public string Password { get; set; } = "";
}

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, StringLength(72)]
    public string Password { get; set; } = "";
}

public sealed record UserDto(int Id, string Username, string Email);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User);
