using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.API.Auth;
using TaskManager.API.Models;

namespace TaskManager.Tests;

public sealed record TestUser(int Id, string Username, string Email, string Token);

/// <summary>A board in a known state, with one user for each way of relating to it.</summary>
public sealed record SeededBoard(
    int BoardId, int TodoId, int DoingId, int DoneId,
    int CardId, byte[] CardRowVersion, int OtherCardId, int LabelId, int CommentId,
    TestUser Owner, TestUser Editor, TestUser Viewer, TestUser Bystander, TestUser Outsider, TestUser Candidate)
{
    public IEnumerable<TestUser> Members => [Owner, Editor, Viewer, Bystander];
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Options))!;
}

public static class BoardTestKit
{
    /// <summary>Users and tokens are created straight in the database; BCrypt is not what these tests are about.</summary>
    public static async Task<SeededBoard> SeedBoardAsync(ApiFactory factory)
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        var tokens = factory.Services.GetRequiredService<ITokenService>();

        return await factory.WithDbAsync(async db =>
        {
            User Make(string who) => new()
            {
                Username = $"{who}_{tag}", Email = $"{who}_{tag}@example.com", PasswordHash = "not-a-real-hash"
            };
            var owner = Make("owner"); var editor = Make("editor"); var viewer = Make("viewer");
            var bystander = Make("bystander"); var outsider = Make("outsider"); var candidate = Make("candidate");
            db.Users.AddRange(owner, editor, viewer, bystander, outsider, candidate);
            await db.SaveChangesAsync();

            var board = new Board { Name = "Seeded", OwnerId = owner.Id };
            board.Members.Add(new BoardMember { UserId = owner.Id, Role = BoardRole.Owner });
            board.Members.Add(new BoardMember { UserId = editor.Id, Role = BoardRole.Editor });
            board.Members.Add(new BoardMember { UserId = viewer.Id, Role = BoardRole.Viewer });
            board.Members.Add(new BoardMember { UserId = bystander.Id, Role = BoardRole.Viewer });
            var todo = new BoardColumn { Name = "To do", Position = 1024 };
            var doing = new BoardColumn { Name = "Doing", Position = 2048 };
            var done = new BoardColumn { Name = "Done", Position = 3072 };
            board.Columns.Add(todo); board.Columns.Add(doing); board.Columns.Add(done);
            db.Boards.Add(board);
            await db.SaveChangesAsync();

            var label = new Label { BoardId = board.Id, Name = "bug", Color = "#FF0000" };
            var card = new TaskItem { BoardId = board.Id, ColumnId = todo.Id, Title = "First", Position = 1024, CreatedById = owner.Id };
            var other = new TaskItem { BoardId = board.Id, ColumnId = todo.Id, Title = "Second", Position = 2048, CreatedById = owner.Id };
            db.Labels.Add(label);
            db.Tasks.AddRange(card, other);
            await db.SaveChangesAsync();

            var comment = new Comment { TaskItemId = card.Id, AuthorId = editor.Id, Body = "seeded comment" };
            db.Comments.Add(comment);
            await db.SaveChangesAsync();

            TestUser Wrap(User u) => new(u.Id, u.Username, u.Email, tokens.CreateAccessToken(u).Value);
            return new SeededBoard(board.Id, todo.Id, doing.Id, done.Id, card.Id, card.RowVersion, other.Id, label.Id, comment.Id,
                Wrap(owner), Wrap(editor), Wrap(viewer), Wrap(bystander), Wrap(outsider), Wrap(candidate));
        });
    }

    public static Task<HttpResponseMessage> SendAsync(
        this HttpClient client, HttpMethod method, string url, string? token = null, object? body = null, string? clientId = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (clientId is not null) request.Headers.Add("X-Client-Id", clientId);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json.Options);
        return client.SendAsync(request);
    }
}
