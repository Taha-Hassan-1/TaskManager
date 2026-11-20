using System.Text.Json;
using TaskManager.API.Data;
using TaskManager.API.Models;

namespace TaskManager.API.Infrastructure;

/// <summary>Something that happened on a board, published after the change is committed.</summary>
public sealed record BoardEvent(
    int BoardId, int ActorId, ActivityType Type, int? TaskItemId = null, object? Payload = null);

/// <summary>Observer: the code that changes a board does not know who is listening (activity log now, SignalR in M5).</summary>
public interface IBoardEventHandler
{
    Task HandleAsync(BoardEvent boardEvent, CancellationToken ct);
}

public interface IBoardEvents
{
    Task PublishAsync(BoardEvent boardEvent, CancellationToken ct);
}

public sealed class BoardEvents(IEnumerable<IBoardEventHandler> handlers, ILogger<BoardEvents> logger) : IBoardEvents
{
    public async Task PublishAsync(BoardEvent boardEvent, CancellationToken ct)
    {
        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(boardEvent, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The change is already committed; a failing listener must not turn it into an error response.
                logger.LogError(ex, "{Handler} failed for {EventType} on board {BoardId}",
                    handler.GetType().Name, boardEvent.Type, boardEvent.BoardId);
            }
        }
    }
}

/// <summary>Appends every event to the board's activity feed.</summary>
public sealed class ActivityLogHandler(AppDbContext db, TimeProvider time) : IBoardEventHandler
{
    public async Task HandleAsync(BoardEvent e, CancellationToken ct)
    {
        db.Activity.Add(new ActivityEntry
        {
            BoardId = e.BoardId,
            ActorId = e.ActorId,
            Type = e.Type,
            TaskItemId = e.TaskItemId,
            Payload = JsonSerializer.Serialize(e.Payload ?? new { }),
            CreatedAt = time.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
    }
}
