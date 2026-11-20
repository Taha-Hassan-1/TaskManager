using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.API.Activity;
using TaskManager.API.Boards;
using TaskManager.API.Columns;
using TaskManager.API.Comments;
using TaskManager.API.Models;
using TaskManager.API.Tasks;

namespace TaskManager.Tests;

public class BoardApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public BoardApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateApiClient();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod m, string url, TestUser? as_, object? body = null) =>
        _client.SendAsync(m, url, as_?.Token, body);

    private async Task<BoardDetailDto> GetBoardAsync(SeededBoard s) =>
        await (await SendAsync(HttpMethod.Get, $"/api/boards/{s.BoardId}", s.Owner)).ReadAsync<BoardDetailDto>();

    private static IEnumerable<string> TitlesIn(BoardDetailDto board, int columnId) =>
        board.Columns.Single(c => c.Id == columnId).Cards.Select(c => c.Title);

    // ---- boards ----

    [Fact]
    public async Task CreateBoard_AddsOwnerAndThreeDefaultColumns()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        var response = await SendAsync(HttpMethod.Post, "/api/boards", s.Outsider, new { name = "  Roadmap  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<BoardSummaryDto>();
        Assert.Equal("Roadmap", created.Name);
        Assert.Equal(BoardRole.Owner, created.MyRole);

        var detail = await (await SendAsync(HttpMethod.Get, $"/api/boards/{created.Id}", s.Outsider)).ReadAsync<BoardDetailDto>();
        Assert.Equal(["To do", "Doing", "Done"], detail.Columns.Select(c => c.Name));
        Assert.Equal(s.Outsider.Id, detail.Members.Single().UserId);
        Assert.Equal(BoardRole.Owner, detail.Members.Single().Role);

        var mine = await (await SendAsync(HttpMethod.Get, "/api/boards", s.Outsider)).ReadAsync<List<BoardSummaryDto>>();
        Assert.Contains(mine, b => b.Id == created.Id);
        var theirs = await (await SendAsync(HttpMethod.Get, "/api/boards", s.Owner)).ReadAsync<List<BoardSummaryDto>>();
        Assert.DoesNotContain(theirs, b => b.Id == created.Id);
    }

    [Fact]
    public async Task CreateBoard_RejectsABlankName()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, "/api/boards", s.Owner, new { name = "   " })).StatusCode);
    }

    [Fact]
    public async Task BoardDetail_ReturnsColumnsCardsMembersAndLabels_AsNamedEnums()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await SendAsync(HttpMethod.Put, $"/api/tasks/{s.CardId}", s.Owner,
            new { title = "First", labelIds = new[] { s.LabelId }, assigneeId = s.Editor.Id, priority = "High", rowVersion = s.CardRowVersion });

        var response = await SendAsync(HttpMethod.Get, $"/api/boards/{s.BoardId}", s.Viewer);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"myRole\":\"Viewer\"", raw);

        var board = await response.ReadAsync<BoardDetailDto>();
        Assert.Equal(["To do", "Doing", "Done"], board.Columns.Select(c => c.Name));
        Assert.Equal(["First", "Second"], TitlesIn(board, s.TodoId));
        var first = board.Columns[0].Cards[0];
        Assert.Equal([s.LabelId], first.LabelIds);
        Assert.Equal(s.Editor.Id, first.AssigneeId);
        Assert.Equal(TaskPriority.High, first.Priority);
        Assert.Equal(1, first.CommentCount);
        Assert.Equal(4, board.Members.Count);
        Assert.Equal("bug", board.Labels.Single().Name);
    }

    [Fact]
    public async Task DeleteBoard_RemovesEverythingUnderIt()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/boards/{s.BoardId}", s.Owner)).StatusCode);

        var left = await _factory.WithDbAsync(async db => new[]
        {
            await db.Columns.CountAsync(c => c.BoardId == s.BoardId), await db.Tasks.CountAsync(t => t.BoardId == s.BoardId),
            await db.Comments.CountAsync(c => c.Id == s.CommentId), await db.Labels.CountAsync(l => l.BoardId == s.BoardId),
            await db.BoardMembers.CountAsync(m => m.BoardId == s.BoardId)
        });
        Assert.All(left, n => Assert.Equal(0, n));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/boards/{s.BoardId}", s.Owner)).StatusCode);
    }

    // ---- members ----

    [Fact]
    public async Task Invite_ByEmail_AddsTheMember_AndRejectsBadInvites()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        var url = $"/api/boards/{s.BoardId}/members";

        var ok = await SendAsync(HttpMethod.Post, url, s.Owner, new { email = s.Candidate.Email.ToUpperInvariant(), role = "Viewer" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(BoardRole.Viewer, (await ok.ReadAsync<MemberDto>()).Role);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/boards/{s.BoardId}", s.Candidate)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Post, url, s.Owner, new { email = s.Candidate.Email })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, url, s.Owner, new { email = "nobody@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, url, s.Owner, new { email = s.Outsider.Email, role = "Owner" })).StatusCode);
    }

    [Fact]
    public async Task ChangeRole_PromotesAViewer_ButNeverTouchesTheOwner()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        var promoted = await SendAsync(HttpMethod.Put, $"/api/boards/{s.BoardId}/members/{s.Viewer.Id}", s.Owner, new { role = "Editor" });
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        var card = await SendAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Viewer, new { title = "Now allowed", columnId = s.TodoId });
        Assert.Equal(HttpStatusCode.Created, card.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict,
            (await SendAsync(HttpMethod.Put, $"/api/boards/{s.BoardId}/members/{s.Owner.Id}", s.Owner, new { role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(HttpMethod.Put, $"/api/boards/{s.BoardId}/members/{s.Viewer.Id}", s.Owner, new { role = "Owner" })).StatusCode);
    }

    [Fact]
    public async Task RemoveMember_UnassignsTheirCards_AndTheOwnerCannotBeRemoved()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await SendAsync(HttpMethod.Put, $"/api/tasks/{s.CardId}", s.Owner, new { title = "First", assigneeId = s.Editor.Id, rowVersion = s.CardRowVersion });

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/boards/{s.BoardId}/members/{s.Editor.Id}", s.Owner)).StatusCode);

        var board = await GetBoardAsync(s);
        Assert.Null(board.Columns[0].Cards.Single(c => c.Id == s.CardId).AssigneeId);
        Assert.DoesNotContain(board.Members, m => m.UserId == s.Editor.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/boards/{s.BoardId}", s.Editor)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Delete, $"/api/boards/{s.BoardId}/members/{s.Owner.Id}", s.Owner)).StatusCode);
    }

    [Fact]
    public async Task AMember_CanLeave_ButNotRemoveSomeoneElse()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Delete, $"/api/boards/{s.BoardId}/members/{s.Bystander.Id}", s.Viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/boards/{s.BoardId}/members/{s.Viewer.Id}", s.Viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/boards/{s.BoardId}", s.Viewer)).StatusCode);
    }

    // ---- columns ----

    [Fact]
    public async Task Columns_AreAddedAtTheEnd_AndCanBeReordered()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        var review = await (await SendAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/columns", s.Editor,
            new { name = "Review", wipLimit = 3 })).ReadAsync<ColumnDto>();
        Assert.Equal(["To do", "Doing", "Done", "Review"], (await GetBoardAsync(s)).Columns.Select(c => c.Name));
        Assert.Equal(3, review.WipLimit);

        var moved = await SendAsync(HttpMethod.Put, $"/api/columns/{review.Id}", s.Editor,
            new { name = "Review", wipLimit = 3, beforeColumnId = s.DoingId });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(["To do", "Review", "Doing", "Done"], (await GetBoardAsync(s)).Columns.Select(c => c.Name));

        var renamed = await SendAsync(HttpMethod.Put, $"/api/columns/{review.Id}", s.Editor, new { name = "QA" });
        Assert.Equal("QA", (await renamed.ReadAsync<ColumnDto>()).Name);
        Assert.Equal(["To do", "QA", "Doing", "Done"], (await GetBoardAsync(s)).Columns.Select(c => c.Name)); // order kept
    }

    // ---- cards ----

    [Fact]
    public async Task CreateCard_GoesToTheBottomOfTheColumn()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        var response = await SendAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Editor,
            new { title = "  Third  ", columnId = s.TodoId, priority = "Urgent", labelIds = new[] { s.LabelId }, assigneeId = s.Viewer.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var card = await response.ReadAsync<CardDto>();
        Assert.Equal("Third", card.Title);
        Assert.Equal(TaskPriority.Urgent, card.Priority);
        Assert.Equal(s.Editor.Id, card.CreatedById);
        Assert.Equal([s.LabelId], card.LabelIds);
        Assert.NotEmpty(card.RowVersion);
        Assert.Equal(["First", "Second", "Third"], TitlesIn(await GetBoardAsync(s), s.TodoId));
    }

    [Fact]
    public async Task CreateCard_RejectsReferencesFromOtherBoards()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        var other = await BoardTestKit.SeedBoardAsync(_factory);
        var url = $"/api/boards/{s.BoardId}/tasks";

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, url, s.Editor, new { title = "x", columnId = other.TodoId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, url, s.Editor, new { title = "x", columnId = s.TodoId, labelIds = new[] { other.LabelId } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, url, s.Editor, new { title = "x", columnId = s.TodoId, assigneeId = s.Outsider.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, url, s.Editor, new { title = "", columnId = s.TodoId })).StatusCode);
    }

    [Fact]
    public async Task CardDetail_IncludesComments()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        var detail = await (await SendAsync(HttpMethod.Get, $"/api/tasks/{s.CardId}", s.Viewer)).ReadAsync<CardDetailDto>();

        Assert.Equal("First", detail.Card.Title);
        var comment = Assert.Single(detail.Comments);
        Assert.Equal("seeded comment", comment.Body);
        Assert.Equal(s.Editor.Username, comment.AuthorName);
    }

    [Fact]
    public async Task UpdateCard_ChangesFields_AndReplacesLabels()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        var response = await SendAsync(HttpMethod.Put, $"/api/tasks/{s.CardId}", s.Editor, new
        {
            title = "Renamed", description = "details", priority = "Low", assigneeId = s.Viewer.Id,
            dueDate = "2027-01-31T00:00:00Z", labelIds = new[] { s.LabelId }, rowVersion = s.CardRowVersion
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var card = await response.ReadAsync<CardDto>();
        Assert.Equal(("Renamed", "details", TaskPriority.Low, s.Viewer.Id), (card.Title, card.Description, card.Priority, card.AssigneeId));
        Assert.Equal([s.LabelId], card.LabelIds);
        Assert.NotEqual(s.CardRowVersion, card.RowVersion);

        var cleared = await (await SendAsync(HttpMethod.Put, $"/api/tasks/{s.CardId}", s.Editor,
            new { title = "Renamed", rowVersion = card.RowVersion })).ReadAsync<CardDto>();
        Assert.Empty(cleared.LabelIds);
        Assert.Null(cleared.AssigneeId);
    }

    [Fact]
    public async Task DeleteCard_RemovesItsCommentsAndLabels()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await SendAsync(HttpMethod.Put, $"/api/tasks/{s.CardId}", s.Editor, new { title = "First", labelIds = new[] { s.LabelId }, rowVersion = s.CardRowVersion });

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/tasks/{s.CardId}", s.Editor)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/tasks/{s.CardId}", s.Editor)).StatusCode);
        Assert.Equal(0, await _factory.WithDbAsync(db => db.Comments.CountAsync(c => c.TaskItemId == s.CardId)));
        Assert.Equal(0, await _factory.WithDbAsync(db => db.TaskLabels.CountAsync(l => l.TaskItemId == s.CardId)));
    }

    [Fact]
    public async Task DeleteLabel_DetachesItFromCards()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await SendAsync(HttpMethod.Put, $"/api/tasks/{s.CardId}", s.Editor, new { title = "First", labelIds = new[] { s.LabelId }, rowVersion = s.CardRowVersion });

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/labels/{s.LabelId}", s.Editor)).StatusCode);

        var board = await GetBoardAsync(s);
        Assert.Empty(board.Labels);
        Assert.Empty(board.Columns[0].Cards.Single(c => c.Id == s.CardId).LabelIds);
    }

    // ---- moving cards ----

    [Fact]
    public async Task Move_WithinAndAcrossColumns_PlacesTheCardWhereAsked()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        // To the end of Doing (empty column).
        var toDoing = await SendAsync(HttpMethod.Patch, $"/api/tasks/{s.CardId}/move", s.Editor, new { columnId = s.DoingId, rowVersion = s.CardRowVersion });
        Assert.Equal(HttpStatusCode.OK, toDoing.StatusCode);
        var moved = await toDoing.ReadAsync<MoveResultDto>();
        Assert.Equal((s.DoingId, 1024d), (moved.ColumnId, moved.Position));

        // Back to To do, above "Second".
        var back = await SendAsync(HttpMethod.Patch, $"/api/tasks/{s.CardId}/move", s.Editor,
            new { columnId = s.TodoId, beforeCardId = s.OtherCardId, rowVersion = moved.RowVersion });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);

        var board = await GetBoardAsync(s);
        Assert.Equal(["First", "Second"], TitlesIn(board, s.TodoId));
        Assert.Empty(TitlesIn(board, s.DoingId));
    }

    [Fact]
    public async Task Move_BetweenTwoCards_UsesTheMidpoint_WithoutTouchingTheOthers()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        var third = await (await SendAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Editor, new { title = "Third", columnId = s.TodoId })).ReadAsync<CardDto>();
        var secondBefore = (await GetBoardAsync(s)).Columns[0].Cards.Single(c => c.Id == s.OtherCardId);

        var result = await (await SendAsync(HttpMethod.Patch, $"/api/tasks/{third.Id}/move", s.Editor,
            new { columnId = s.TodoId, afterCardId = s.CardId, rowVersion = third.RowVersion })).ReadAsync<MoveResultDto>();

        Assert.Equal(1536, result.Position); // halfway between 1024 and 2048
        var board = await GetBoardAsync(s);
        Assert.Equal(["First", "Third", "Second"], TitlesIn(board, s.TodoId));
        Assert.Equal(secondBefore.RowVersion, board.Columns[0].Cards.Single(c => c.Id == s.OtherCardId).RowVersion); // one row written
    }

    [Fact]
    public async Task Move_WhenTheGapIsUsedUp_RenumbersTheColumnAndKeepsTheOrder()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        await _factory.WithDbAsync(async db =>
        {
            await db.Tasks.Where(t => t.Id == s.OtherCardId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Position, 1024 + 1e-7));
            return 0;
        });
        var third = await (await SendAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Editor, new { title = "Third", columnId = s.TodoId })).ReadAsync<CardDto>();

        var response = await SendAsync(HttpMethod.Patch, $"/api/tasks/{third.Id}/move", s.Editor,
            new { columnId = s.TodoId, beforeCardId = s.OtherCardId, rowVersion = third.RowVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cards = (await GetBoardAsync(s)).Columns[0].Cards;
        Assert.Equal(["First", "Third", "Second"], cards.Select(c => c.Title));
        Assert.Equal(3, cards.Select(c => c.Position).Distinct().Count());
        Assert.True(cards.Zip(cards.Skip(1), (a, b) => b.Position - a.Position).All(gap => gap > 1));
    }

    [Fact]
    public async Task Move_RejectsBadTargets()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        var other = await BoardTestKit.SeedBoardAsync(_factory);
        var url = $"/api/tasks/{s.CardId}/move";

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Patch, url, s.Editor, new { columnId = other.TodoId, rowVersion = s.CardRowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Patch, url, s.Editor, new { columnId = s.DoingId, beforeCardId = s.OtherCardId, rowVersion = s.CardRowVersion })).StatusCode); // not in Doing
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Patch, url, s.Editor, new { columnId = s.TodoId, beforeCardId = 1, afterCardId = 2, rowVersion = s.CardRowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Patch, url, s.Editor, new { columnId = s.TodoId })).StatusCode); // no rowVersion
    }

    // ---- concurrency ----

    [Fact]
    public async Task Move_WithAStaleRowVersion_Returns409_AndTheCardStaysPut()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        var url = $"/api/tasks/{s.CardId}/move";

        // Alice and Bob both hold version 1 of the card. Alice moves it first.
        var alice = await SendAsync(HttpMethod.Patch, url, s.Owner, new { columnId = s.DoingId, rowVersion = s.CardRowVersion });
        Assert.Equal(HttpStatusCode.OK, alice.StatusCode);
        var current = await alice.ReadAsync<MoveResultDto>();

        var bob = await SendAsync(HttpMethod.Patch, url, s.Editor, new { columnId = s.DoneId, rowVersion = s.CardRowVersion });

        Assert.Equal(HttpStatusCode.Conflict, bob.StatusCode);
        var problem = await bob.ReadAsync<ProblemDetails>();
        Assert.Equal(409, problem.Status);
        Assert.Equal(s.DoingId, (await GetBoardAsync(s)).Columns.Single(c => c.Cards.Any(x => x.Id == s.CardId)).Id);

        // After reloading, Bob's move goes through.
        var retry = await SendAsync(HttpMethod.Patch, url, s.Editor, new { columnId = s.DoneId, rowVersion = current.RowVersion });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Update_WithAStaleRowVersion_Returns409()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        var first = await SendAsync(HttpMethod.Put, $"/api/tasks/{s.CardId}", s.Owner, new { title = "Alice's title", rowVersion = s.CardRowVersion });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await SendAsync(HttpMethod.Put, $"/api/tasks/{s.CardId}", s.Editor, new { title = "Bob's title", rowVersion = s.CardRowVersion });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Alice's title", (await (await SendAsync(HttpMethod.Get, $"/api/tasks/{s.CardId}", s.Owner)).ReadAsync<CardDetailDto>()).Card.Title);
    }

    [Fact]
    public async Task Update_ThatOnlyChangesLabels_StillChecksAndBumpsTheVersion()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        var labelled = await (await SendAsync(HttpMethod.Put, $"/api/tasks/{s.CardId}", s.Owner,
            new { title = "First", labelIds = new[] { s.LabelId }, rowVersion = s.CardRowVersion })).ReadAsync<CardDto>();
        Assert.NotEqual(s.CardRowVersion, labelled.RowVersion);

        var stale = await SendAsync(HttpMethod.Put, $"/api/tasks/{s.CardId}", s.Editor, new { title = "First", rowVersion = s.CardRowVersion });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal([s.LabelId], (await GetBoardAsync(s)).Columns[0].Cards.Single(c => c.Id == s.CardId).LabelIds);
    }

    [Fact]
    public async Task Move_TwoSimultaneousRequestsWithTheSameVersion_OnlyOneWins()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        var url = $"/api/tasks/{s.CardId}/move";

        var results = await Task.WhenAll(
            SendAsync(HttpMethod.Patch, url, s.Owner, new { columnId = s.DoingId, rowVersion = s.CardRowVersion }),
            SendAsync(HttpMethod.Patch, url, s.Editor, new { columnId = s.DoneId, rowVersion = s.CardRowVersion }));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    // ---- comments ----

    [Fact]
    public async Task Comments_AuthorEdits_OwnerOrAuthorDeletes()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        var created = await SendAsync(HttpMethod.Post, $"/api/tasks/{s.CardId}/comments", s.Editor, new { body = "  Looks good  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var comment = await created.ReadAsync<CommentDto>();
        Assert.Equal(("Looks good", s.Editor.Username), (comment.Body, comment.AuthorName));

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put, $"/api/comments/{comment.Id}", s.Owner, new { body = "hijacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/comments/{comment.Id}", s.Editor, new { body = "Edited" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Delete, $"/api/comments/{comment.Id}", s.Viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/comments/{comment.Id}", s.Owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/comments/{s.CommentId}", s.Editor)).StatusCode);
    }

    [Fact]
    public async Task Comment_RejectsAnEmptyOrOversizedBody()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        var url = $"/api/tasks/{s.CardId}/comments";

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, url, s.Editor, new { body = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, url, s.Editor, new { body = new string('x', 2001) })).StatusCode);
    }

    // ---- activity ----

    [Fact]
    public async Task Activity_RecordsWhatHappened_NewestFirst()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        var created = await (await SendAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Editor, new { title = "Tracked", columnId = s.TodoId })).ReadAsync<CardDto>();
        await SendAsync(HttpMethod.Patch, $"/api/tasks/{created.Id}/move", s.Editor, new { columnId = s.DoingId, rowVersion = created.RowVersion });
        await SendAsync(HttpMethod.Post, $"/api/tasks/{created.Id}/comments", s.Editor, new { body = "done soon" });

        var page = await (await SendAsync(HttpMethod.Get, $"/api/boards/{s.BoardId}/activity", s.Viewer)).ReadAsync<ActivityPageDto>();

        Assert.Equal([ActivityType.CommentAdded, ActivityType.TaskMoved, ActivityType.TaskCreated], page.Items.Select(a => a.Type));
        Assert.All(page.Items, a => Assert.Equal(s.Editor.Username, a.ActorName));
        Assert.Equal(created.Id, page.Items[0].TaskItemId);
        Assert.Equal(s.DoingId, page.Items[1].Payload.GetProperty("toColumnId").GetInt32());
        Assert.Equal("Tracked", page.Items[2].Payload.GetProperty("title").GetString());
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Activity_PagesWithACursor_WithoutGapsOrRepeats()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        for (var i = 1; i <= 5; i++)
            await SendAsync(HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", s.Editor, new { title = $"Card {i}", columnId = s.TodoId });

        var titles = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = $"/api/boards/{s.BoardId}/activity?limit=2" + (cursor is null ? "" : $"&cursor={cursor}");
            var page = await (await SendAsync(HttpMethod.Get, url, s.Viewer)).ReadAsync<ActivityPageDto>();
            Assert.True(page.Items.Count <= 2);
            titles.AddRange(page.Items.Select(a => a.Payload.GetProperty("title").GetString()!));
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null && pages < 10);

        Assert.Equal(["Card 5", "Card 4", "Card 3", "Card 2", "Card 1"], titles);
        Assert.Equal(3, pages);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Get, $"/api/boards/{s.BoardId}/activity?cursor=garbage", s.Viewer)).StatusCode);
    }

    [Fact]
    public async Task Activity_CoversBoardAndMemberEvents()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);
        var board = await (await SendAsync(HttpMethod.Post, "/api/boards", s.Outsider, new { name = "Events" })).ReadAsync<BoardSummaryDto>();
        await SendAsync(HttpMethod.Post, $"/api/boards/{board.Id}/members", s.Outsider, new { email = s.Candidate.Email, role = "Viewer" });

        var page = await (await SendAsync(HttpMethod.Get, $"/api/boards/{board.Id}/activity", s.Outsider)).ReadAsync<ActivityPageDto>();

        Assert.Equal([ActivityType.MemberAdded, ActivityType.BoardCreated], page.Items.Select(a => a.Type));
    }
}
