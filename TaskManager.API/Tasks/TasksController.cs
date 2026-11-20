using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.API.Boards;
using TaskManager.API.Comments;
using TaskManager.API.Data;
using TaskManager.API.Infrastructure;
using TaskManager.API.Models;

namespace TaskManager.API.Tasks;

public sealed record CardDetailDto(CardDto Card, IReadOnlyList<CommentDto> Comments);

public sealed class TasksController(
    AppDbContext db, IAuthorizationService authorization, IBoardEvents events,
    IPositionCalculator positions, TimeProvider time, ILogger<TasksController> logger)
    : BoardControllerBase(db, authorization, events)
{
    [HttpPost("boards/{boardId:int}/tasks")]
    public async Task<ActionResult<CardDto>> Create(int boardId, CreateCardRequest request, CancellationToken ct)
    {
        if (await DenyUnlessAsync(boardId, BoardAccess.Editor) is { } denied) return denied;

        if (!await Db.Columns.AnyAsync(c => c.Id == request.ColumnId && c.BoardId == boardId, ct))
            ModelState.AddModelError(nameof(request.ColumnId), "The column is not on this board.");
        await ValidateReferencesAsync(boardId, request.AssigneeId, request.LabelIds, ct);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // Append to the bottom of the column.
        var last = await Db.Tasks.Where(t => t.ColumnId == request.ColumnId)
            .Select(t => (double?)t.Position).MaxAsync(ct);

        var task = new TaskItem
        {
            BoardId = boardId,
            ColumnId = request.ColumnId,
            Title = request.Title.Trim(),
            Description = request.Description,
            DueDate = request.DueDate,
            Priority = request.Priority,
            AssigneeId = request.AssigneeId,
            CreatedById = UserId,
            CreatedAt = time.GetUtcNow().UtcDateTime,
            Position = positions.Between(last, null)!.Value
        };
        foreach (var labelId in request.LabelIds.Distinct()) task.Labels.Add(new TaskLabel { LabelId = labelId });

        Db.Tasks.Add(task);
        await Db.SaveChangesAsync(ct);
        await Events.PublishAsync(new BoardEvent(boardId, UserId, ActivityType.TaskCreated, task.Id,
            new { title = task.Title, columnId = task.ColumnId }), ct);

        return Created($"/api/tasks/{task.Id}", await ToCardAsync(task, ct));
    }

    [HttpGet("tasks/{id:int}")]
    public async Task<ActionResult<CardDetailDto>> Get(int id, CancellationToken ct)
    {
        var task = await Db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return NotFoundProblem();
        if (await DenyUnlessAsync(task.BoardId, BoardAccess.Viewer) is { } denied) return denied;

        var comments = await Db.Comments.AsNoTracking()
            .Where(c => c.TaskItemId == id)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => new CommentDto(c.Id, c.TaskItemId, c.AuthorId, c.Author.Username, c.Body, c.CreatedAt))
            .ToListAsync(ct);

        return Ok(new CardDetailDto(await ToCardAsync(task, ct), comments));
    }

    [HttpPut("tasks/{id:int}")]
    public async Task<ActionResult<CardDto>> Update(int id, UpdateCardRequest request, CancellationToken ct)
    {
        var task = await Db.Tasks.Include(t => t.Labels).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return NotFoundProblem();
        if (await DenyUnlessAsync(task.BoardId, BoardAccess.Editor) is { } denied) return denied;

        if (!task.RowVersion.SequenceEqual(request.RowVersion)) return StaleCard(task, request.RowVersion);

        await ValidateReferencesAsync(task.BoardId, request.AssigneeId, request.LabelIds, ct);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        task.Title = request.Title.Trim();
        task.Description = request.Description;
        task.DueDate = request.DueDate;
        task.Priority = request.Priority;
        task.AssigneeId = request.AssigneeId;

        var wanted = request.LabelIds.Distinct().ToHashSet();
        foreach (var existing in task.Labels.Where(l => !wanted.Contains(l.LabelId)).ToList())
            Db.TaskLabels.Remove(existing);
        foreach (var labelId in wanted.Where(w => task.Labels.All(l => l.LabelId != w)))
            task.Labels.Add(new TaskLabel { LabelId = labelId });

        // A labels-only edit would not touch the card row, and so skip the concurrency check; force the UPDATE.
        Db.Entry(task).Property(t => t.Title).IsModified = true;
        Db.Entry(task).Property(t => t.RowVersion).OriginalValue = request.RowVersion;

        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StaleCard(task, request.RowVersion);
        }

        await Events.PublishAsync(new BoardEvent(task.BoardId, UserId, ActivityType.TaskUpdated, task.Id,
            new { title = task.Title }), ct);
        return Ok(await ToCardAsync(task, ct));
    }

    /// <summary>Move to a column and a place in it. Updates one row; the column is renumbered only when its gaps run out.</summary>
    [HttpPatch("tasks/{id:int}/move")]
    public async Task<ActionResult<MoveResultDto>> Move(int id, MoveCardRequest request, CancellationToken ct)
    {
        var task = await Db.Tasks.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return NotFoundProblem();
        if (await DenyUnlessAsync(task.BoardId, BoardAccess.Editor) is { } denied) return denied;

        if (!task.RowVersion.SequenceEqual(request.RowVersion)) return StaleCard(task, request.RowVersion);

        if (request.BeforeCardId is not null && request.AfterCardId is not null)
            ModelState.AddModelError(nameof(request.AfterCardId), "Give beforeCardId or afterCardId, not both.");
        else if (!await Db.Columns.AnyAsync(c => c.Id == request.ColumnId && c.BoardId == task.BoardId, ct))
            ModelState.AddModelError(nameof(request.ColumnId), "The column is not on this board.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var siblings = await Db.Tasks.AsNoTracking()
            .Where(t => t.ColumnId == request.ColumnId && t.Id != id)
            .OrderBy(t => t.Position).ThenBy(t => t.Id)
            .Select(t => new Sibling(t.Id, t.Position))
            .ToListAsync(ct);

        var placement = positions.Place(siblings, request.BeforeCardId, request.AfterCardId);
        if (!placement.Valid)
        {
            ModelState.AddModelError(nameof(request.BeforeCardId), "The referenced card is not in the target column.");
            return ValidationProblem(ModelState);
        }

        if (placement.Position is null)
        {
            // Rare: the gap between neighbours is used up. Renumber the target column once, then place again.
            var column = await Db.Tasks.Where(t => t.ColumnId == request.ColumnId && t.Id != id)
                .OrderBy(t => t.Position).ThenBy(t => t.Id).ToListAsync(ct);
            for (var i = 0; i < column.Count; i++) column[i].Position = (i + 1) * positions.Step;
            siblings = column.Select(t => new Sibling(t.Id, t.Position)).ToList();
            placement = positions.Place(siblings, request.BeforeCardId, request.AfterCardId);
        }

        var fromColumnId = task.ColumnId;
        task.ColumnId = request.ColumnId;
        task.Position = placement.Position!.Value;
        Db.Entry(task).Property(t => t.RowVersion).OriginalValue = request.RowVersion;

        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StaleCard(task, request.RowVersion);
        }

        await Events.PublishAsync(new BoardEvent(task.BoardId, UserId, ActivityType.TaskMoved, task.Id,
            new { fromColumnId, toColumnId = task.ColumnId }), ct);
        return Ok(new MoveResultDto(task.Id, task.ColumnId, task.Position, task.RowVersion));
    }

    [HttpDelete("tasks/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var task = await Db.Tasks.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return NotFoundProblem();
        if (await DenyUnlessAsync(task.BoardId, BoardAccess.Editor) is { } denied) return denied;

        Db.Tasks.Remove(task);
        await Db.SaveChangesAsync(ct);
        await Events.PublishAsync(new BoardEvent(task.BoardId, UserId, ActivityType.TaskDeleted, task.Id,
            new { title = task.Title }), ct);
        return NoContent();
    }

    // ---- labels (they belong to one board and are attached to cards through UpdateCardRequest.LabelIds) ----

    [HttpPost("boards/{boardId:int}/labels")]
    public async Task<ActionResult<LabelDto>> CreateLabel(int boardId, CreateLabelRequest request, CancellationToken ct)
    {
        if (await DenyUnlessAsync(boardId, BoardAccess.Editor) is { } denied) return denied;

        var label = new Label { BoardId = boardId, Name = request.Name.Trim(), Color = request.Color };
        Db.Labels.Add(label);
        await Db.SaveChangesAsync(ct);
        return Created($"/api/boards/{boardId}", new LabelDto(label.Id, label.Name, label.Color));
    }

    [HttpDelete("labels/{id:int}")]
    public async Task<IActionResult> DeleteLabel(int id, CancellationToken ct)
    {
        var label = await Db.Labels.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct);
        if (label is null) return NotFoundProblem();
        if (await DenyUnlessAsync(label.BoardId, BoardAccess.Editor) is { } denied) return denied;

        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        await Db.TaskLabels.Where(tl => tl.LabelId == id).ExecuteDeleteAsync(ct);
        await Db.Labels.Where(l => l.Id == id).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    // ---- helpers ----

    /// <summary>The assignee must be a member of the board and every label must belong to it.</summary>
    private async Task ValidateReferencesAsync(int boardId, int? assigneeId, IReadOnlyCollection<int> labelIds, CancellationToken ct)
    {
        if (assigneeId is { } assignee
            && !await Db.BoardMembers.AnyAsync(m => m.BoardId == boardId && m.UserId == assignee, ct))
            ModelState.AddModelError("assigneeId", "The assignee must be a member of the board.");

        var distinct = labelIds.Distinct().ToList();
        if (distinct.Count > 0
            && await Db.Labels.CountAsync(l => l.BoardId == boardId && distinct.Contains(l.Id), ct) != distinct.Count)
            ModelState.AddModelError("labelIds", "Every label must belong to this board.");
    }

    private async Task<CardDto> ToCardAsync(TaskItem t, CancellationToken ct)
    {
        var labelIds = await Db.TaskLabels.AsNoTracking()
            .Where(tl => tl.TaskItemId == t.Id).Select(tl => tl.LabelId).ToListAsync(ct);
        var commentCount = await Db.Comments.CountAsync(c => c.TaskItemId == t.Id, ct);
        return new CardDto(t.Id, t.BoardId, t.ColumnId, t.Title, t.Description, t.Position, t.Priority, t.DueDate,
            t.AssigneeId, t.CreatedById, t.CreatedAt, labelIds, commentCount, t.RowVersion);
    }

    private ObjectResult StaleCard(TaskItem task, byte[] clientVersion)
    {
        // Both versions in the log make "two people moved the same card" easy to reproduce.
        logger.LogWarning("Stale RowVersion on card {TaskId}: client sent {ClientVersion}, database has {CurrentVersion}",
            task.Id, Convert.ToBase64String(clientVersion), Convert.ToBase64String(task.RowVersion));
        return ConflictProblem("The card was changed by someone else.", "Reload the card and try again.");
    }
}
