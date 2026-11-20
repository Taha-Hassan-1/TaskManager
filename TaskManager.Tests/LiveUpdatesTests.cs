using Microsoft.AspNetCore.SignalR;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using TaskManager.API.Boards;
using TaskManager.API.Tasks;

namespace TaskManager.Tests;

/// <summary>The hub end to end: real SignalR clients against the test server, changes made through REST.</summary>
public class LiveUpdatesTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _http;

    public LiveUpdatesTests(ApiFactory factory)
    {
        _factory = factory;
        _http = factory.CreateApiClient();
    }

    private Task<HttpResponseMessage> RestAsync(HttpMethod m, string url, TestUser who, object? body = null, string? clientId = null) =>
        _http.SendAsync(m, url, who.Token, body, clientId);

    private async Task<HubTestClient> WatchAsync(SeededBoard s, TestUser who)
    {
        var client = new HubTestClient(_factory, who.Token);
        await client.StartAsync();
        await client.JoinAsync(s.BoardId);
        return client;
    }

    // ---- joining ----

    [Fact]
    public async Task EveryoneOnTheBoard_ReceivesAMove_AfterItIsCommitted()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await using var owner = await WatchAsync(s, s.Owner);
        await using var viewer = await WatchAsync(s, s.Viewer); // read-only members watch too

        var response = await RestAsync(HttpMethod.Patch, $"/api/tasks/{s.CardId}/move", s.Editor,
            new { columnId = s.DoingId, rowVersion = s.CardRowVersion }, clientId: "tab-editor");
        var moved = await response.ReadAsync<MoveResultDto>();

        foreach (var client in new[] { owner, viewer })
        {
            var message = (await client.WaitForAsync("TaskMoved")).Body;
            Assert.Equal(s.CardId, message.GetProperty("taskId").GetInt32());
            Assert.Equal(s.TodoId, message.GetProperty("fromColumnId").GetInt32());
            Assert.Equal(s.DoingId, message.GetProperty("columnId").GetInt32());
            Assert.Equal(moved.Position, message.GetProperty("position").GetDouble());
            Assert.Equal(Convert.ToBase64String(moved.RowVersion), message.GetProperty("rowVersion").GetString());
            Assert.Equal("tab-editor", message.GetProperty("originClientId").GetString());
        }
    }

    [Fact]
    public async Task APush_NeverRevealsMoreThanTheChange()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await using var watcher = await WatchAsync(s, s.Owner);

        await RestAsync(HttpMethod.Patch, $"/api/tasks/{s.CardId}/move", s.Editor, new { columnId = s.DoingId, rowVersion = s.CardRowVersion });

        var body = (await watcher.WaitForAsync("TaskMoved")).Body;
        Assert.Equal(["boardId", "columnId", "fromColumnId", "originClientId", "position", "rowVersion", "taskId"],
            body.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task ANonMember_CannotJoin_AndHearsNothing()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await using var outsider = new HubTestClient(_factory, s.Outsider.Token);
        await outsider.StartAsync(); // a valid token connects...

        var ex = await Assert.ThrowsAsync<HubException>(() => outsider.JoinAsync(s.BoardId)); // ...but joining a board is checked
        Assert.Contains("Board not found", ex.Message);

        await RestAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Editor, new { title = "Secret", columnId = s.TodoId });
        await outsider.ExpectNoneAsync("TaskCreated");
    }

    [Fact]
    public async Task ABoardThatDoesNotExist_GivesTheSameAnswerAsOneThatIsNotYours()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await using var client = new HubTestClient(_factory, s.Outsider.Token);
        await client.StartAsync();

        var notYours = await Assert.ThrowsAsync<HubException>(() => client.JoinAsync(s.BoardId));
        var missing = await Assert.ThrowsAsync<HubException>(() => client.JoinAsync(int.MaxValue));

        Assert.Equal(notYours.Message, missing.Message);
    }

    [Fact]
    public async Task Connecting_RequiresAValidToken()
    {
        await using var anonymous = new HubTestClient(_factory, token: null);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => anonymous.StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);

        await using var forged = new HubTestClient(_factory, "not.a.token");
        await Assert.ThrowsAsync<HttpRequestException>(() => forged.StartAsync());
    }

    [Fact]
    public async Task OnlyTheBoardsOwnGroup_ReceivesItsChanges()
    {
        var a = await BoardTestKit.SeedBoardAsync(_factory);
        var b = await BoardTestKit.SeedBoardAsync(_factory);
        await using var watchingA = await WatchAsync(a, a.Owner);
        await using var watchingB = await WatchAsync(b, b.Owner);

        await RestAsync(HttpMethod.Post, $"/api/boards/{a.BoardId}/tasks", a.Owner, new { title = "On A", columnId = a.TodoId });

        await watchingA.WaitForAsync("TaskCreated");
        await watchingB.ExpectNoneAsync("TaskCreated");
    }

    [Fact]
    public async Task LeavingABoard_StopsTheFlow()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await using var watcher = await WatchAsync(s, s.Owner);
        await watcher.Connection.InvokeAsync("LeaveBoard", s.BoardId);

        await RestAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Editor, new { title = "Nobody listening", columnId = s.TodoId });

        await watcher.ExpectNoneAsync("TaskCreated");
    }

    [Fact]
    public async Task TwoTabsOfTheSameUser_BothReceive()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await using var tab1 = await WatchAsync(s, s.Owner);
        await using var tab2 = await WatchAsync(s, s.Owner);

        await RestAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Owner, new { title = "Shared", columnId = s.TodoId }, clientId: "tab-1");

        await tab1.WaitForAsync("TaskCreated"); // each tab decides for itself, from originClientId, whether it is an echo
        await tab2.WaitForAsync("TaskCreated");
    }

    [Fact]
    public async Task TheTokenInTheQueryString_WorksForTheHubAndNowhereElse()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        var rest = await _http.GetAsync($"/api/boards?access_token={s.Owner.Token}");

        Assert.Equal(HttpStatusCode.Unauthorized, rest.StatusCode);
    }

    // ---- what gets pushed ----

    [Fact]
    public async Task Cards_ArePushedWithTheirFields()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await using var watcher = await WatchAsync(s, s.Viewer);

        var created = await (await RestAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Editor,
            new { title = "Live card", columnId = s.TodoId, priority = "High" })).ReadAsync<CardDto>();
        var pushedCreate = (await watcher.WaitForAsync("TaskCreated")).Body.GetProperty("card");
        Assert.Equal(created.Id, pushedCreate.GetProperty("id").GetInt32());
        Assert.Equal("High", pushedCreate.GetProperty("priority").GetString());

        var updated = await (await RestAsync(HttpMethod.Put, $"/api/tasks/{created.Id}", s.Editor,
            new { title = "Renamed live", rowVersion = created.RowVersion })).ReadAsync<CardDto>();
        var pushedUpdate = (await watcher.WaitForAsync("TaskUpdated")).Body.GetProperty("card");
        Assert.Equal("Renamed live", pushedUpdate.GetProperty("title").GetString());
        Assert.Equal(Convert.ToBase64String(updated.RowVersion), pushedUpdate.GetProperty("rowVersion").GetString());

        await RestAsync(HttpMethod.Delete, $"/api/tasks/{created.Id}", s.Editor);
        var pushedDelete = (await watcher.WaitForAsync("TaskDeleted")).Body;
        Assert.Equal(created.Id, pushedDelete.GetProperty("taskId").GetInt32());
    }

    [Fact]
    public async Task ColumnsLabelsCommentsMembersAndTheBoard_ArePushedToo()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await using var watcher = await WatchAsync(s, s.Viewer);

        var column = await (await RestAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/columns", s.Editor, new { name = "Review", wipLimit = 2 })).ReadAsync<ColumnDto>();
        var pushedColumn = (await watcher.WaitForAsync("ColumnChanged")).Body.GetProperty("column");
        Assert.Equal(("Review", 2), (pushedColumn.GetProperty("name").GetString(), pushedColumn.GetProperty("wipLimit").GetInt32()));
        Assert.Equal(column.Id, pushedColumn.GetProperty("id").GetInt32());

        var label = await (await RestAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/labels", s.Editor, new { name = "urgent", color = "#FF0000" })).ReadAsync<LabelDto>();
        Assert.Equal("urgent", (await watcher.WaitForAsync("LabelChanged", b => b.GetProperty("label").ValueKind == JsonValueKind.Object)).Body.GetProperty("label").GetProperty("name").GetString());
        await RestAsync(HttpMethod.Delete, $"/api/labels/{label.Id}", s.Editor);
        await watcher.WaitForAsync("LabelChanged", b => b.GetProperty("label").ValueKind == JsonValueKind.Null && b.GetProperty("labelId").GetInt32() == label.Id);

        var comment = await (await RestAsync(HttpMethod.Post, $"/api/tasks/{s.CardId}/comments", s.Editor, new { body = "hello" })).ReadAsync<API.Comments.CommentDto>();
        var pushedComment = (await watcher.WaitForAsync("CommentAdded")).Body;
        Assert.Equal((s.CardId, "hello"), (pushedComment.GetProperty("taskId").GetInt32(), pushedComment.GetProperty("comment").GetProperty("body").GetString()));
        await RestAsync(HttpMethod.Put, $"/api/comments/{comment.Id}", s.Editor, new { body = "edited" });
        await watcher.WaitForAsync("CommentChanged", b => b.GetProperty("comment").ValueKind == JsonValueKind.Object);
        await RestAsync(HttpMethod.Delete, $"/api/comments/{comment.Id}", s.Editor);
        await watcher.WaitForAsync("CommentChanged", b => b.GetProperty("comment").ValueKind == JsonValueKind.Null);

        await RestAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/members", s.Owner, new { email = s.Candidate.Email, role = "Viewer" });
        var pushedMember = (await watcher.WaitForAsync("MemberChanged")).Body;
        Assert.Equal(s.Candidate.Id, pushedMember.GetProperty("userId").GetInt32());
        Assert.Equal("Viewer", pushedMember.GetProperty("member").GetProperty("role").GetString());

        await RestAsync(HttpMethod.Put, $"/api/boards/{s.BoardId}", s.Owner, new { name = "Renamed board" });
        Assert.Equal("Renamed board", (await watcher.WaitForAsync("BoardChanged", b => b.GetProperty("name").GetString() == "Renamed board")).Body.GetProperty("name").GetString());

        await RestAsync(HttpMethod.Delete, $"/api/boards/{s.BoardId}", s.Owner);
        await watcher.WaitForAsync("BoardDeleted");
    }

    [Fact]
    public async Task EachActivityEntry_IsPushedAsItIsWritten()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await using var watcher = await WatchAsync(s, s.Viewer);

        await RestAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Editor, new { title = "Tracked live", columnId = s.TodoId });

        var entry = (await watcher.WaitForAsync("ActivityAdded")).Body.GetProperty("entry");
        Assert.Equal("TaskCreated", entry.GetProperty("type").GetString());
        Assert.Equal(s.Editor.Username, entry.GetProperty("actorName").GetString());
        Assert.Equal("Tracked live", entry.GetProperty("payload").GetProperty("title").GetString());
        Assert.True(entry.GetProperty("id").GetInt64() > 0);
    }

    // ---- losing access while connected ----

    [Fact]
    public async Task ARemovedMember_IsToldThenCutOff_WhileOthersCarryOn()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await using var removed = await WatchAsync(s, s.Bystander);
        await using var staying = await WatchAsync(s, s.Viewer);

        await RestAsync(HttpMethod.Delete, $"/api/boards/{s.BoardId}/members/{s.Bystander.Id}", s.Owner);

        var told = (await removed.WaitForAsync("MemberChanged")).Body; // they hear that they were removed...
        Assert.Equal(s.Bystander.Id, told.GetProperty("userId").GetInt32());
        Assert.Equal(JsonValueKind.Null, told.GetProperty("member").ValueKind);

        await RestAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Editor, new { title = "After removal", columnId = s.TodoId });
        await staying.WaitForAsync("TaskCreated"); // everyone else still gets updates
        await removed.ExpectNoneAsync("TaskCreated"); // ...and nothing after that

        var ex = await Assert.ThrowsAsync<HubException>(() => removed.JoinAsync(s.BoardId)); // and cannot rejoin
        Assert.Contains("Board not found", ex.Message);
    }
}
