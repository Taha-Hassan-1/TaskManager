using System.Collections.Concurrent;

namespace TeamBoard.API.Realtime;

/// <summary>
/// Which connections have joined which boards. SignalR groups cannot be listed, and removing a member
/// has to cut that person's live connections off the board's group.
/// </summary>
public sealed class BoardConnections
{
    private sealed record Joined(int UserId, HashSet<int> Boards);

    private readonly ConcurrentDictionary<string, Joined> _connections = new();

    public void Join(string connectionId, int userId, int boardId)
    {
        var joined = _connections.GetOrAdd(connectionId, _ => new Joined(userId, []));
        lock (joined.Boards) joined.Boards.Add(boardId);
    }

    public void Leave(string connectionId, int boardId)
    {
        if (!_connections.TryGetValue(connectionId, out var joined)) return;
        lock (joined.Boards) joined.Boards.Remove(boardId);
    }

    public void Disconnect(string connectionId) => _connections.TryRemove(connectionId, out _);

    public IReadOnlyList<string> ConnectionsOf(int userId, int boardId) =>
        _connections
            .Where(kv => kv.Value.UserId == userId && Contains(kv.Value, boardId))
            .Select(kv => kv.Key)
            .ToList();

    private static bool Contains(Joined joined, int boardId)
    {
        lock (joined.Boards) return joined.Boards.Contains(boardId);
    }
}
