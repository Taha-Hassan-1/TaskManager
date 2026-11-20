namespace TaskManager.API.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BoardMember> Memberships { get; set; } = new List<BoardMember>();
    public ICollection<TaskItem> CreatedTasks { get; set; } = new List<TaskItem>();
}
