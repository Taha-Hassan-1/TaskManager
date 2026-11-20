using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace TaskManager.Tests;

/// <summary>A SignalR client against the in-memory test server that records everything the hub pushes.</summary>
public sealed class HubTestClient : IAsyncDisposable
{
    public static readonly string[] Methods =
    [
        "TaskCreated", "TaskUpdated", "TaskMoved", "TaskDeleted", "ColumnChanged", "CommentAdded", "CommentChanged",
        "MemberChanged", "LabelChanged", "BoardChanged", "BoardDeleted", "ActivityAdded",
    ];

    public sealed record Message(string Method, JsonElement Body);

    private readonly ConcurrentQueue<Message> _received = new();
    public HubConnection Connection { get; }

    public IReadOnlyList<Message> Received => _received.ToArray();

    public HubTestClient(ApiFactory factory, string? token)
    {
        Connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/board"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling; // the in-memory server has no real sockets
                if (token is not null) options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        foreach (var method in Methods)
            Connection.On<JsonElement>(method, body => _received.Enqueue(new Message(method, body)));
    }

    public Task StartAsync() => Connection.StartAsync();

    public async Task JoinAsync(int boardId) => await Connection.InvokeAsync("JoinBoard", boardId);

    /// <summary>The next message of this kind that satisfies the filter, within a few seconds.</summary>
    public async Task<Message> WaitForAsync(string method, Func<JsonElement, bool>? where = null, int timeoutMs = 5000, int pollMs = 20)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var hit = _received.FirstOrDefault(m => m.Method == method && (where?.Invoke(m.Body) ?? true));
            if (hit is not null) return hit;
            await Task.Delay(pollMs);
        }
        throw new TimeoutException($"No {method} arrived. Received: {string.Join(", ", _received.Select(m => m.Method))}");
    }

    /// <summary>Asserts that nothing of this kind shows up, after giving the server time to send it.</summary>
    public async Task ExpectNoneAsync(string method, int waitMs = 400)
    {
        await Task.Delay(waitMs);
        Assert.DoesNotContain(_received, m => m.Method == method);
    }

    public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
}
