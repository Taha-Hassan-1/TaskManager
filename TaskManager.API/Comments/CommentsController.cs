using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.API.Data;
using TaskManager.API.Infrastructure;
using TaskManager.API.Models;

namespace TaskManager.API.Comments;

public sealed class CommentRequest
{
    [Required, StringLength(2000, MinimumLength = 1)]
    public string Body { get; set; } = "";
}

public sealed record CommentDto(int Id, int TaskItemId, int AuthorId, string AuthorName, string Body, DateTime CreatedAt);

public sealed class CommentsController(
    AppDbContext db, IAuthorizationService authorization, IBoardEvents events, TimeProvider time)
    : BoardControllerBase(db, authorization, events)
{
    [HttpPost("tasks/{taskId:int}/comments")]
    public async Task<ActionResult<CommentDto>> Create(int taskId, CommentRequest request, CancellationToken ct)
    {
        var boardId = await Db.Tasks.AsNoTracking().Where(t => t.Id == taskId)
            .Select(t => (int?)t.BoardId).SingleOrDefaultAsync(ct);
        if (boardId is null) return NotFoundProblem();
        if (await DenyUnlessAsync(boardId.Value, BoardAccess.Editor) is { } denied) return denied;

        var comment = new Comment
        {
            TaskItemId = taskId,
            AuthorId = UserId,
            Body = request.Body.Trim(),
            CreatedAt = time.GetUtcNow().UtcDateTime
        };
        Db.Comments.Add(comment);
        await Db.SaveChangesAsync(ct);
        await Events.PublishAsync(new BoardEvent(boardId.Value, UserId, ActivityType.CommentAdded, taskId,
            new { commentId = comment.Id }), ct);

        var author = await Db.Users.AsNoTracking().Where(u => u.Id == UserId).Select(u => u.Username).SingleAsync(ct);
        return Created($"/api/tasks/{taskId}", new CommentDto(comment.Id, taskId, UserId, author, comment.Body, comment.CreatedAt));
    }

    /// <summary>Only the author edits a comment.</summary>
    [HttpPut("comments/{id:int}")]
    public async Task<ActionResult<CommentDto>> Update(int id, CommentRequest request, CancellationToken ct)
    {
        var row = await FindAsync(id, ct);
        if (row is null) return NotFoundProblem();
        var (comment, boardId) = row.Value;

        if (await DenyUnlessAsync(boardId, BoardAccess.Viewer) is { } denied) return denied;
        if (comment.AuthorId != UserId)
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only the author can edit a comment.");

        comment.Body = request.Body.Trim();
        await Db.SaveChangesAsync(ct);

        var author = await Db.Users.AsNoTracking().Where(u => u.Id == comment.AuthorId).Select(u => u.Username).SingleAsync(ct);
        return Ok(new CommentDto(comment.Id, comment.TaskItemId, comment.AuthorId, author, comment.Body, comment.CreatedAt));
    }

    /// <summary>The author deletes their own comment; the board owner can delete any.</summary>
    [HttpDelete("comments/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var row = await FindAsync(id, ct);
        if (row is null) return NotFoundProblem();
        var (comment, boardId) = row.Value;

        var requirement = comment.AuthorId == UserId ? BoardAccess.Viewer : BoardAccess.Owner;
        if (await DenyUnlessAsync(boardId, requirement) is { } denied) return denied;

        Db.Comments.Remove(comment);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<(Comment Comment, int BoardId)?> FindAsync(int id, CancellationToken ct)
    {
        var comment = await Db.Comments.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (comment is null) return null;
        var boardId = await Db.Tasks.AsNoTracking().Where(t => t.Id == comment.TaskItemId).Select(t => t.BoardId).SingleAsync(ct);
        return (comment, boardId);
    }
}
