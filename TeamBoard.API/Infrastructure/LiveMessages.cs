using TeamBoard.API.Activity;
using TeamBoard.API.Boards;
using TeamBoard.API.Comments;

namespace TeamBoard.API.Infrastructure;

/// <summary>
/// What the hub pushes to a board's group, after the change is committed. Messages carry ids and the fields
/// that changed, never whole boards. Every message says which client caused it (the X-Client-Id header of the
/// request), so that client can ignore its own echo.
/// </summary>
public static class LiveMethods
{
    public const string TaskCreated = nameof(TaskCreated);
    public const string TaskUpdated = nameof(TaskUpdated);
    public const string TaskMoved = nameof(TaskMoved);
    public const string TaskDeleted = nameof(TaskDeleted);
    public const string ColumnChanged = nameof(ColumnChanged);
    public const string CommentAdded = nameof(CommentAdded);
    public const string CommentChanged = nameof(CommentChanged);
    public const string MemberChanged = nameof(MemberChanged);
    public const string LabelChanged = nameof(LabelChanged);
    public const string BoardChanged = nameof(BoardChanged);
    public const string BoardDeleted = nameof(BoardDeleted);
    public const string ActivityAdded = nameof(ActivityAdded);
}

public sealed record TaskCreatedMessage(int BoardId, CardDto Card, string? OriginClientId);
public sealed record TaskUpdatedMessage(int BoardId, CardDto Card, string? OriginClientId);

/// <summary>Only what a move changes; the client already holds the rest of the card.</summary>
public sealed record TaskMovedMessage(
    int BoardId, int TaskId, int FromColumnId, int ColumnId, double Position, byte[] RowVersion, string? OriginClientId);

public sealed record TaskDeletedMessage(int BoardId, int TaskId, string? OriginClientId);
public sealed record ColumnChangedMessage(int BoardId, ColumnDto Column, string? OriginClientId);
public sealed record CommentAddedMessage(int BoardId, int TaskId, CommentDto Comment, string? OriginClientId);

/// <summary>An edited comment, or (Comment is null) a deleted one.</summary>
public sealed record CommentChangedMessage(int BoardId, int TaskId, int CommentId, CommentDto? Comment, string? OriginClientId);

/// <summary>A new or changed member, or (Member is null) one who left or was removed.</summary>
public sealed record MemberChangedMessage(int BoardId, int UserId, MemberDto? Member, string? OriginClientId);

/// <summary>A new or changed label, or (Label is null) a deleted one.</summary>
public sealed record LabelChangedMessage(int BoardId, int LabelId, LabelDto? Label, string? OriginClientId);

public sealed record BoardChangedMessage(int BoardId, string Name, string? OriginClientId);
public sealed record BoardDeletedMessage(int BoardId, string? OriginClientId);
public sealed record ActivityAddedMessage(int BoardId, ActivityDto Entry);
