namespace TaskManager.API.Models;

// Only the hash of the token is stored. Reuse of a rotated token revokes the user's active tokens (M2).
public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public int? ReplacedByTokenId { get; set; }
}
