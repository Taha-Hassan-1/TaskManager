using Microsoft.AspNetCore.SignalR;
using TaskManager.API.Infrastructure;

namespace TaskManager.API.Realtime;

/// <summary>Sends to a board's group. A seam so the rest of the code never touches SignalR types.</summary>
public interface IBoardBroadcaster
{
    Task SendAsync(int boardId, string method, object body, CancellationToken ct);

    /// <summary>Takes a user's live connections off a board's group (they were removed from the board).</summary>
    Task RemoveUserAsync(int boardId, int userId, CancellationToken ct);
}

public sealed class SignalRBoardBroadcaster(IHubContext<BoardHub> hub, BoardConnections connections) : IBoardBroadcaster
{
    public Task SendAsync(int boardId, string method, object body, CancellationToken ct) =>
        hub.Clients.Group(BoardHub.GroupName(boardId)).SendAsync(method, body, ct);

    public async Task RemoveUserAsync(int boardId, int userId, CancellationToken ct)
    {
        foreach (var connectionId in connections.ConnectionsOf(userId, boardId))
        {
            await hub.Groups.RemoveFromGroupAsync(connectionId, BoardHub.GroupName(boardId), ct);
            connections.Leave(connectionId, boardId);
        }
    }
}

/// <summary>The SignalR listener on the board events: pushes each event's live message to the board's group.</summary>
public sealed class SignalRNotifier(IBoardBroadcaster broadcaster) : IBoardEventHandler
{
    public async Task HandleAsync(BoardEvent e, CancellationToken ct)
    {
        if (e.Live is not { } live) return;

        await broadcaster.SendAsync(e.BoardId, live.Method, live.Body, ct);

        // The removed member hears about it first, then stops receiving anything from this board.
        if (live.Body is MemberChangedMessage { Member: null } removed)
            await broadcaster.RemoveUserAsync(e.BoardId, removed.UserId, ct);
    }
}
