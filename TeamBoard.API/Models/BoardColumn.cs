namespace TeamBoard.API.Models;

// Named BoardColumn to avoid clashing with System.Data / EF "Column" types; the table is "Columns".
public class BoardColumn
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public Board Board { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public double Position { get; set; }
    // Soft limit: the UI warns, the API does not refuse.
    public int? WipLimit { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}
