namespace TeamBoard.API.Models;

// Append-only. TaskItemId is a plain id, not a foreign key, so history survives a deleted card.
public class ActivityEntry
{
    public long Id { get; set; }
    public int BoardId { get; set; }
    public Board Board { get; set; } = null!;
    public int ActorId { get; set; }
    public User Actor { get; set; } = null!;
    public ActivityType Type { get; set; }
    public int? TaskItemId { get; set; }
    public string Payload { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
