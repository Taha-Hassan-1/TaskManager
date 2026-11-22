namespace TeamBoard.API.Models;

public class Comment
{
    public int Id { get; set; }
    public int TaskItemId { get; set; }
    public TaskItem TaskItem { get; set; } = null!;
    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
