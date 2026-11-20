using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.API.Boards;
using TaskManager.API.Data;
using TaskManager.API.Infrastructure;
using TaskManager.API.Models;

namespace TaskManager.API.Columns;

public sealed class ColumnRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = "";

    /// <summary>Soft limit: the UI warns, the API does not refuse.</summary>
    [Range(1, 1000)]
    public int? WipLimit { get; set; }

    /// <summary>Place the column before or after this one. On create the default is the end; on update, unchanged.</summary>
    public int? BeforeColumnId { get; set; }
    public int? AfterColumnId { get; set; }
}

public sealed class ColumnsController(
    AppDbContext db, IAuthorizationService authorization, IBoardEvents events, IPositionCalculator positions)
    : BoardControllerBase(db, authorization, events)
{
    [HttpPost("boards/{boardId:int}/columns")]
    public async Task<ActionResult<ColumnDto>> Create(int boardId, ColumnRequest request, CancellationToken ct)
    {
        if (await DenyUnlessAsync(boardId, BoardAccess.Editor) is { } denied) return denied;

        var column = new BoardColumn { BoardId = boardId, Name = request.Name.Trim(), WipLimit = request.WipLimit };
        if (await PlaceAsync(column, request, ct) is { } invalid) return invalid;

        Db.Columns.Add(column);
        await Db.SaveChangesAsync(ct);
        var dto = new ColumnDto(column.Id, column.Name, column.Position, column.WipLimit, []);
        await PublishAsync(boardId, ActivityType.ColumnCreated, LiveMethods.ColumnChanged,
            origin => new ColumnChangedMessage(boardId, dto, origin), ct, payload: new { columnId = column.Id, name = column.Name });

        return Created($"/api/boards/{boardId}", dto);
    }

    [HttpPut("columns/{id:int}")]
    public async Task<ActionResult<ColumnDto>> Update(int id, ColumnRequest request, CancellationToken ct)
    {
        var column = await Db.Columns.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (column is null) return NotFoundProblem();
        if (await DenyUnlessAsync(column.BoardId, BoardAccess.Editor) is { } denied) return denied;

        column.Name = request.Name.Trim();
        column.WipLimit = request.WipLimit;
        if ((request.BeforeColumnId is not null || request.AfterColumnId is not null)
            && await PlaceAsync(column, request, ct) is { } invalid) return invalid;

        await Db.SaveChangesAsync(ct);
        var dto = new ColumnDto(column.Id, column.Name, column.Position, column.WipLimit, []);
        await PublishAsync(column.BoardId, ActivityType.ColumnUpdated, LiveMethods.ColumnChanged,
            origin => new ColumnChangedMessage(column.BoardId, dto, origin), ct, payload: new { columnId = column.Id, name = column.Name });

        return Ok(dto);
    }

    /// <summary>Sets <paramref name="column"/>.Position from the before/after hint; returns a problem when the hint is bad.</summary>
    private async Task<ActionResult?> PlaceAsync(BoardColumn column, ColumnRequest request, CancellationToken ct)
    {
        if (request.BeforeColumnId is not null && request.AfterColumnId is not null)
        {
            ModelState.AddModelError(nameof(request.AfterColumnId), "Give beforeColumnId or afterColumnId, not both.");
            return ValidationProblem(ModelState);
        }

        var siblings = await Db.Columns.AsNoTracking()
            .Where(c => c.BoardId == column.BoardId && c.Id != column.Id)
            .OrderBy(c => c.Position).ThenBy(c => c.Id)
            .Select(c => new Sibling(c.Id, c.Position))
            .ToListAsync(ct);

        var placement = positions.Place(siblings, request.BeforeColumnId, request.AfterColumnId);
        if (!placement.Valid)
        {
            ModelState.AddModelError(nameof(request.BeforeColumnId), "The referenced column is not on this board.");
            return ValidationProblem(ModelState);
        }

        if (placement.Position is null)
        {
            // Gap exhausted: renumber this board's columns, then place again.
            var others = await Db.Columns.Where(c => c.BoardId == column.BoardId && c.Id != column.Id)
                .OrderBy(c => c.Position).ThenBy(c => c.Id).ToListAsync(ct);
            for (var i = 0; i < others.Count; i++) others[i].Position = (i + 1) * positions.Step;
            siblings = others.Select(c => new Sibling(c.Id, c.Position)).ToList();
            placement = positions.Place(siblings, request.BeforeColumnId, request.AfterColumnId);
        }

        column.Position = placement.Position!.Value;
        return null;
    }
}
