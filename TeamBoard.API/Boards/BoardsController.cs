using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamBoard.API.Data;
using TeamBoard.API.Infrastructure;
using TeamBoard.API.Models;

namespace TeamBoard.API.Boards;

public sealed class BoardsController(
    AppDbContext db, IAuthorizationService authorization, IBoardEvents events,
    IPositionCalculator positions, TimeProvider time)
    : BoardControllerBase(db, authorization, events)
{
    [HttpGet("boards")]
    public async Task<ActionResult<IReadOnlyList<BoardSummaryDto>>> List(CancellationToken ct)
    {
        var me = UserId;
        var boards = await Db.BoardMembers.AsNoTracking()
            .Where(m => m.UserId == me)
            .OrderBy(m => m.Board.Name)
            .Select(m => new BoardSummaryDto(
                m.BoardId, m.Board.Name, m.Role, m.Board.OwnerId, m.Board.CreatedAt, m.Board.Tasks.Count()))
            .ToListAsync(ct);
        return Ok(boards);
    }

    [HttpPost("boards")]
    public async Task<ActionResult<BoardSummaryDto>> Create(BoardNameRequest request, CancellationToken ct)
    {
        var board = new Board
        {
            Name = request.Name.Trim(),
            OwnerId = UserId,
            CreatedAt = time.GetUtcNow().UtcDateTime
        };
        board.Members.Add(new BoardMember { UserId = UserId, Role = BoardRole.Owner });
        string[] defaults = ["To do", "Doing", "Done"];
        for (var i = 0; i < defaults.Length; i++)
            board.Columns.Add(new BoardColumn { Name = defaults[i], Position = (i + 1) * positions.Step });

        Db.Boards.Add(board);
        await Db.SaveChangesAsync(ct);
        await PublishAsync(board.Id, ActivityType.BoardCreated, LiveMethods.BoardChanged,
            origin => new BoardChangedMessage(board.Id, board.Name, origin), ct, payload: new { name = board.Name });

        var dto = new BoardSummaryDto(board.Id, board.Name, BoardRole.Owner, board.OwnerId, board.CreatedAt, 0);
        return Created($"/api/boards/{board.Id}", dto);
    }

    /// <summary>The board with its columns, cards, members and labels in one response.</summary>
    [HttpGet("boards/{id:int}")]
    public async Task<ActionResult<BoardDetailDto>> Get(int id, CancellationToken ct)
    {
        if (await DenyUnlessAsync(id, BoardAccess.Viewer) is { } denied) return denied;

        // A handful of flat, tracking-free projections instead of one entity graph; a single join over
        // columns, cards and labels would multiply rows.
        var board = await Db.Boards.AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new { b.Id, b.Name, b.OwnerId })
            .SingleAsync(ct);

        var members = await Db.BoardMembers.AsNoTracking()
            .Where(m => m.BoardId == id)
            .OrderBy(m => m.User.Username)
            .Select(m => new MemberDto(m.UserId, m.User.Username, m.User.Email, m.Role))
            .ToListAsync(ct);
        var me = UserId;
        var myRole = members.Single(m => m.UserId == me).Role; // authorised above, so I am in the list: no separate query

        var labels = await Db.Labels.AsNoTracking()
            .Where(l => l.BoardId == id).OrderBy(l => l.Name)
            .Select(l => new LabelDto(l.Id, l.Name, l.Color))
            .ToListAsync(ct);

        var columns = await Db.Columns.AsNoTracking()
            .Where(c => c.BoardId == id).OrderBy(c => c.Position).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Name, c.Position, c.WipLimit })
            .ToListAsync(ct);

        var cards = await Db.Tasks.AsNoTracking()
            .Where(t => t.BoardId == id)
            .OrderBy(t => t.Position).ThenBy(t => t.Id) // equal positions (simultaneous adds) stay in a stable order
            .Select(t => new
            {
                t.Id, t.ColumnId, t.Title, t.Description, t.Position, t.Priority, t.DueDate,
                t.AssigneeId, t.CreatedById, t.CreatedAt, t.RowVersion,
                CommentCount = t.Comments.Count()
            })
            .ToListAsync(ct);

        var labelIdsByCard = (await Db.TaskLabels.AsNoTracking()
                .Where(tl => tl.TaskItem.BoardId == id)
                .Select(tl => new { tl.TaskItemId, tl.LabelId })
                .ToListAsync(ct))
            .GroupBy(x => x.TaskItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(x => x.LabelId).ToList());

        var cardsByColumn = cards
            .Select(t => new CardDto(
                t.Id, id, t.ColumnId, t.Title, t.Description, t.Position, t.Priority, t.DueDate,
                t.AssigneeId, t.CreatedById, t.CreatedAt,
                labelIdsByCard.GetValueOrDefault(t.Id, []), t.CommentCount, t.RowVersion))
            .GroupBy(c => c.ColumnId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CardDto>)g.ToList());

        var columnDtos = columns
            .Select(c => new ColumnDto(c.Id, c.Name, c.Position, c.WipLimit, cardsByColumn.GetValueOrDefault(c.Id, [])))
            .ToList();

        return Ok(new BoardDetailDto(board.Id, board.Name, board.OwnerId, myRole, members, columnDtos, labels));
    }

    [HttpPut("boards/{id:int}")]
    public async Task<ActionResult<BoardSummaryDto>> Rename(int id, BoardNameRequest request, CancellationToken ct)
    {
        if (await DenyUnlessAsync(id, BoardAccess.Owner) is { } denied) return denied;

        var board = await Db.Boards.SingleAsync(b => b.Id == id, ct);
        board.Name = request.Name.Trim();
        await Db.SaveChangesAsync(ct);
        await PublishAsync(id, ActivityType.BoardRenamed, LiveMethods.BoardChanged,
            origin => new BoardChangedMessage(id, board.Name, origin), ct, payload: new { name = board.Name });

        var cardCount = await Db.Tasks.CountAsync(t => t.BoardId == id, ct);
        return Ok(new BoardSummaryDto(board.Id, board.Name, BoardRole.Owner, board.OwnerId, board.CreatedAt, cardCount));
    }

    [HttpDelete("boards/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (await DenyUnlessAsync(id, BoardAccess.Owner) is { } denied) return denied;

        // The database cascades down the tree: members, columns, cards, comments, labels, activity.
        await Db.Boards.Where(b => b.Id == id).ExecuteDeleteAsync(ct);
        // No activity entry: the feed went with the board. Everyone still looking at it is told to leave.
        await PublishAsync(id, null, LiveMethods.BoardDeleted, origin => new BoardDeletedMessage(id, origin), ct);
        return NoContent();
    }
}
