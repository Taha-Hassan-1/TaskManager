using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamBoard.API.Data;
using TeamBoard.API.Infrastructure;
using TeamBoard.API.Models;

namespace TeamBoard.API.Activity;

public sealed record ActivityDto(
    long Id, ActivityType Type, int ActorId, string ActorName, int? TaskItemId, JsonElement Payload, DateTime CreatedAt);

public sealed record ActivityPageDto(IReadOnlyList<ActivityDto> Items, string? NextCursor);

public sealed class ActivityController(AppDbContext db, IAuthorizationService authorization, IBoardEvents events)
    : BoardControllerBase(db, authorization, events)
{
    private const int DefaultPageSize = 30;
    private const int MaxPageSize = 100;

    /// <summary>Newest first. Pass the previous page's nextCursor to continue.</summary>
    [HttpGet("boards/{boardId:int}/activity")]
    public async Task<ActionResult<ActivityPageDto>> List(
        int boardId, string? cursor, int limit = DefaultPageSize, CancellationToken ct = default)
    {
        if (await DenyUnlessAsync(boardId, BoardAccess.Viewer) is { } denied) return denied;
        limit = Math.Clamp(limit, 1, MaxPageSize);

        var query = Db.Activity.AsNoTracking().Where(a => a.BoardId == boardId);
        if (cursor is not null)
        {
            if (!TryParseCursor(cursor, out var at, out var beforeId))
            {
                ModelState.AddModelError(nameof(cursor), "The cursor is not valid.");
                return ValidationProblem(ModelState);
            }
            query = query.Where(a => a.CreatedAt < at || (a.CreatedAt == at && a.Id < beforeId));
        }

        // Served by the (BoardId, CreatedAt) index; one extra row tells whether another page exists.
        var rows = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(limit + 1)
            .Select(a => new { a.Id, a.Type, a.ActorId, ActorName = a.Actor.Username, a.TaskItemId, a.Payload, a.CreatedAt })
            .ToListAsync(ct);

        var page = rows.Take(limit).ToList();
        var next = rows.Count > limit ? $"{page[^1].CreatedAt.Ticks}-{page[^1].Id}" : null;
        var items = page.Select(a => new ActivityDto(
            a.Id, a.Type, a.ActorId, a.ActorName, a.TaskItemId,
            JsonDocument.Parse(a.Payload).RootElement.Clone(), a.CreatedAt)).ToList();

        return Ok(new ActivityPageDto(items, next));
    }

    private static bool TryParseCursor(string cursor, out DateTime at, out long id)
    {
        at = default;
        id = 0;
        var parts = cursor.Split('-');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out id)
            || ticks is < 0 or > 3_155_378_975_999_999_999L)
            return false;

        at = new DateTime(ticks, DateTimeKind.Unspecified);
        return true;
    }
}
