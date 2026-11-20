using System.ComponentModel.DataAnnotations;
using TaskManager.API.Models;

namespace TaskManager.API.Boards;

public sealed class BoardNameRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = "";
}

public sealed class InviteMemberRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    /// <summary>Editor or Viewer. There is exactly one Owner per board.</summary>
    public BoardRole Role { get; set; } = BoardRole.Editor;
}

public sealed class ChangeMemberRoleRequest
{
    public BoardRole Role { get; set; }
}

public sealed record BoardSummaryDto(int Id, string Name, BoardRole MyRole, int OwnerId, DateTime CreatedAt, int CardCount);

public sealed record MemberDto(int UserId, string Username, string Email, BoardRole Role);

public sealed record LabelDto(int Id, string Name, string Color);

public sealed record CardDto(
    int Id, int BoardId, int ColumnId, string Title, string? Description, double Position,
    TaskPriority Priority, DateTime? DueDate, int? AssigneeId, int CreatedById, DateTime CreatedAt,
    IReadOnlyList<int> LabelIds, int CommentCount, byte[] RowVersion);

public sealed record ColumnDto(int Id, string Name, double Position, int? WipLimit, IReadOnlyList<CardDto> Cards);

/// <summary>Everything the board screen needs in one response.</summary>
public sealed record BoardDetailDto(
    int Id, string Name, int OwnerId, BoardRole MyRole,
    IReadOnlyList<MemberDto> Members, IReadOnlyList<ColumnDto> Columns, IReadOnlyList<LabelDto> Labels);
