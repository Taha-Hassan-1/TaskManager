namespace TeamBoard.API.Models;

public class Label
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public Board Board { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#888888";
}

public class TaskLabel
{
    public int TaskItemId { get; set; }
    public TaskItem TaskItem { get; set; } = null!;
    public int LabelId { get; set; }
    public Label Label { get; set; } = null!;
}
