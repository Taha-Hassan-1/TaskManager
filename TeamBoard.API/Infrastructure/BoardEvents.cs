using System.Text.Json;
using TeamBoard.API.Activity;
using TeamBoard.API.Data;
using TeamBoard.API.Models;
using TeamBoard.API.Realtime;

namespace TeamBoard.API.Infrastructure;

/// <summary>What the hub should push for an event: the client method and its message.</summary>
public sealed record LiveMessage(string Method, object Body);

/// <summary>
/// Something that happened on a board, published after the change is committed.
/// <see cref="Type"/> is the activity-feed entry it becomes (null: nothing worth listing, e.g. a label);
/// <see cref="Live"/> is what to push to the people looking at the board (null: nothing).
/// </summary>
public sealed record BoardEvent(
    int BoardId, int ActorId, string ActorName, ActivityType? Type, int? TaskItemId = null,
    object? Payload = null, LiveMessage? Live = null, string? OriginClientId = null);

/// <summary>Observer: the code that changes a board does not know who is listening (activity log, SignalR).</summary>
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

/// <summary>Appends events to the board's activity feed and pushes the new entry to people watching it.</summary>
public sealed class ActivityLogHandler(AppDbContext db, TimeProvider time, IBoardBroadcaster broadcaster) : IBoardEventHandler
{
    // Same conventions as the API (enums as names), so the stored payload reads the same as every other response.
    private static readonly JsonSerializerOptions PayloadOptions = CreatePayloadOptions();

    private static JsonSerializerOptions CreatePayloadOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonConfig.Apply(options);
        return options;
    }

    public async Task HandleAsync(BoardEvent e, CancellationToken ct)
    {
        if (e.Type is not { } type) return;

        var entry = new ActivityEntry
        {
            BoardId = e.BoardId,
            ActorId = e.ActorId,
            Type = type,
            TaskItemId = e.TaskItemId,
            Payload = JsonSerializer.Serialize(e.Payload ?? new { }, PayloadOptions),
            CreatedAt = time.GetUtcNow().UtcDateTime
        };
        db.Activity.Add(entry);
        await db.SaveChangesAsync(ct);

        var dto = new ActivityDto(
            entry.Id, type, e.ActorId, e.ActorName, e.TaskItemId,
            JsonSerializer.SerializeToElement(e.Payload ?? new { }, PayloadOptions), entry.CreatedAt);
        await broadcaster.SendAsync(e.BoardId, LiveMethods.ActivityAdded, new ActivityAddedMessage(e.BoardId, dto), ct);
    }
}
