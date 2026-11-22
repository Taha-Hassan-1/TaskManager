using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TeamBoard.API.Infrastructure;

namespace TeamBoard.API.Realtime;

/// <summary>
/// Clients only listen: every change goes through REST, where validation and permissions live, and the
/// server then pushes the result here. The one thing a client asks of the hub is to join a board's group.
/// </summary>
[Authorize]
public sealed class BoardHub(IAuthorizationService authorization, BoardConnections connections) : Hub
{
    public static string GroupName(int boardId) => $"board-{boardId}";

    /// <summary>Membership is checked again on every join; the token alone proves nothing about a board.</summary>
    public async Task JoinBoard(int boardId)
    {
        var allowed = await authorization.AuthorizeAsync(Context.User!, boardId, BoardAccess.Viewer);
        // Same answer for "no such board" and "not yours", so ids cannot be probed.
        if (!allowed.Succeeded) throw new HubException("Board not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(boardId));
        connections.Join(Context.ConnectionId, UserId, boardId);
    }

    public async Task LeaveBoard(int boardId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(boardId));
        connections.Leave(Context.ConnectionId, boardId);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Disconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private int UserId => int.Parse(Context.User!.FindFirst("sub")!.Value);
}
