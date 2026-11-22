using System.ComponentModel.DataAnnotations;

namespace TeamBoard.API.Models;

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int BoardId { get; set; }
    public Board Board { get; set; } = null!;
    public int ColumnId { get; set; }
    public BoardColumn Column { get; set; } = null!;

    // Gap-based ordering inside a column: a move updates one row (see IPositionCalculator, M3).
    public double Position { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    public int? AssigneeId { get; set; }
    public User? Assignee { get; set; }

    // Was UserId; "done" is now expressed by the card sitting in a Done column, so IsCompleted is gone.
    public int CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    // Optimistic concurrency: a stale move or edit returns 409.
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public ICollection<TaskLabel> Labels { get; set; } = new List<TaskLabel>();
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
}
