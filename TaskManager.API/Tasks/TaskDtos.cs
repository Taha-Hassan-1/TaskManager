using System.ComponentModel.DataAnnotations;
using TaskManager.API.Models;

namespace TaskManager.API.Tasks;

public sealed class CreateCardRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = "";

    [StringLength(4000)]
    public string? Description { get; set; }

    public int ColumnId { get; set; }
    public DateTime? DueDate { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public int? AssigneeId { get; set; }
    public List<int> LabelIds { get; set; } = [];
}

public sealed class UpdateCardRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = "";

    [StringLength(4000)]
    public string? Description { get; set; }

    public DateTime? DueDate { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public int? AssigneeId { get; set; }
    public List<int> LabelIds { get; set; } = [];

    /// <summary>The version the client last saw; a stale value is answered with 409.</summary>
    [MinLength(1, ErrorMessage = "rowVersion is required.")]
    public byte[] RowVersion { get; set; } = [];
}

public sealed class MoveCardRequest
{
    public int ColumnId { get; set; }

    /// <summary>Drop the card just above / just below this card. Neither: the end of the column.</summary>
    public int? BeforeCardId { get; set; }
    public int? AfterCardId { get; set; }

    [MinLength(1, ErrorMessage = "rowVersion is required.")]
    public byte[] RowVersion { get; set; } = [];
}

public sealed class CreateLabelRequest
{
    [Required, StringLength(50, MinimumLength = 1)]
    public string Name { get; set; } = "";

    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color must look like #1A2B3C.")]
    public string Color { get; set; } = "";
}

/// <summary>Deliberately small: a move is the hot path, so it returns only what changed.</summary>
public sealed record MoveResultDto(int Id, int ColumnId, double Position, byte[] RowVersion);
