namespace TeamBoard.API.Models;

public class Board
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BoardMember> Members { get; set; } = new List<BoardMember>();
    public ICollection<BoardColumn> Columns { get; set; } = new List<BoardColumn>();
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
    public ICollection<Label> Labels { get; set; } = new List<Label>();
}
