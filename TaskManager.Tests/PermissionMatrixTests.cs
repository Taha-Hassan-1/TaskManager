using System.Net;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.API.Infrastructure;
using TaskManager.API.Models;

namespace TaskManager.Tests;

/// <summary>
/// Every board endpoint against every way of relating to a board: anonymous, non-member, Viewer, Editor, Owner.
/// A fresh board is seeded for each cell, so destructive endpoints can be exercised freely.
/// </summary>
public class PermissionMatrixTests : IClassFixture<ApiFactory>
{
    private sealed record Endpoint(
        string Key,                                   // "METHOD route-template", as the framework reports it
        BoardRole? Minimum,                           // null: any signed-in user, board membership not involved
        Func<SeededBoard, (HttpMethod Method, string Url, object? Body)> Build,
        Func<BoardRole, bool>? Allowed = null)        // for rules that are not "this role or higher"
    {
        public string Name => Key;
        public bool IsAllowed(BoardRole role) => Allowed?.Invoke(role) ?? role >= Minimum;
    }

    private static readonly Endpoint[] Endpoints =
    [
        new("GET api/boards", null, s => (HttpMethod.Get, "/api/boards", null)),
        new("POST api/boards", null, s => (HttpMethod.Post, "/api/boards", new { name = "New board" })),
        new("GET api/boards/{id:int}", BoardRole.Viewer, s => (HttpMethod.Get, $"/api/boards/{s.BoardId}", null)),
        new("PUT api/boards/{id:int}", BoardRole.Owner, s => (HttpMethod.Put, $"/api/boards/{s.BoardId}", new { name = "Renamed" })),
        new("DELETE api/boards/{id:int}", BoardRole.Owner, s => (HttpMethod.Delete, $"/api/boards/{s.BoardId}", null)),

        new("POST api/boards/{boardId:int}/members", BoardRole.Owner,
            s => (HttpMethod.Post, $"/api/boards/{s.BoardId}/members", new { email = s.Candidate.Email, role = "Editor" })),
        new("PUT api/boards/{boardId:int}/members/{userId:int}", BoardRole.Owner,
            s => (HttpMethod.Put, $"/api/boards/{s.BoardId}/members/{s.Bystander.Id}", new { role = "Editor" })),
        new("DELETE api/boards/{boardId:int}/members/{userId:int}", BoardRole.Owner,
            s => (HttpMethod.Delete, $"/api/boards/{s.BoardId}/members/{s.Bystander.Id}", null)),

        new("POST api/boards/{boardId:int}/columns", BoardRole.Editor,
            s => (HttpMethod.Post, $"/api/boards/{s.BoardId}/columns", new { name = "Review" })),
        new("PUT api/columns/{id:int}", BoardRole.Editor,
            s => (HttpMethod.Put, $"/api/columns/{s.TodoId}", new { name = "Backlog" })),

        new("POST api/boards/{boardId:int}/tasks", BoardRole.Editor,
            s => (HttpMethod.Post, $"/api/boards/{s.BoardId}/tasks", new { title = "New card", columnId = s.TodoId })),
        new("GET api/tasks/{id:int}", BoardRole.Viewer, s => (HttpMethod.Get, $"/api/tasks/{s.CardId}", null)),
        new("PUT api/tasks/{id:int}", BoardRole.Editor,
            s => (HttpMethod.Put, $"/api/tasks/{s.CardId}", new { title = "Edited", rowVersion = s.CardRowVersion })),
        new("PATCH api/tasks/{id:int}/move", BoardRole.Editor,
            s => (HttpMethod.Patch, $"/api/tasks/{s.CardId}/move", new { columnId = s.DoingId, rowVersion = s.CardRowVersion })),
        new("DELETE api/tasks/{id:int}", BoardRole.Editor, s => (HttpMethod.Delete, $"/api/tasks/{s.CardId}", null)),

        new("POST api/boards/{boardId:int}/labels", BoardRole.Editor,
            s => (HttpMethod.Post, $"/api/boards/{s.BoardId}/labels", new { name = "ui", color = "#00FF00" })),
        new("DELETE api/labels/{id:int}", BoardRole.Editor, s => (HttpMethod.Delete, $"/api/labels/{s.LabelId}", null)),

        new("POST api/tasks/{taskId:int}/comments", BoardRole.Editor,
            s => (HttpMethod.Post, $"/api/tasks/{s.CardId}/comments", new { body = "hello" })),
        // The seeded comment was written by the Editor: only the author edits it.
        new("PUT api/comments/{id:int}", BoardRole.Viewer,
            s => (HttpMethod.Put, $"/api/comments/{s.CommentId}", new { body = "edited" }), role => role == BoardRole.Editor),
        // ...while the author or the Owner can delete it.
        new("DELETE api/comments/{id:int}", BoardRole.Viewer,
            s => (HttpMethod.Delete, $"/api/comments/{s.CommentId}", null), role => role is BoardRole.Editor or BoardRole.Owner),

        new("GET api/boards/{boardId:int}/activity", BoardRole.Viewer, s => (HttpMethod.Get, $"/api/boards/{s.BoardId}/activity", null)),
    ];

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public PermissionMatrixTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateApiClient();
    }

    public static TheoryData<string, string> Cells()
    {
        var data = new TheoryData<string, string>();
        foreach (var endpoint in Endpoints)
            foreach (var actor in new[] { "Anonymous", "Outsider", "Viewer", "Editor", "Owner" })
                data.Add(endpoint.Key, actor);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task Endpoint_EnforcesBoardPermissions(string endpointKey, string actor)
    {
        var endpoint = Endpoints.Single(e => e.Key == endpointKey);
        var seeded = await BoardTestKit.SeedBoardAsync(_factory);
        var (method, url, body) = endpoint.Build(seeded);

        var token = actor switch
        {
            "Anonymous" => null,
            "Outsider" => seeded.Outsider.Token,
            "Viewer" => seeded.Viewer.Token,
            "Editor" => seeded.Editor.Token,
            _ => seeded.Owner.Token
        };

        var response = await _client.SendAsync(method, url, token, body);

        HttpStatusCode? expected = (actor, endpoint.Minimum) switch
        {
            ("Anonymous", _) => HttpStatusCode.Unauthorized,
            (_, null) => null,                          // any signed-in user: must succeed
            ("Outsider", _) => HttpStatusCode.NotFound,                  // not a member: the board is not even visible
            _ => endpoint.IsAllowed(Enum.Parse<BoardRole>(actor)) ? null : HttpStatusCode.Forbidden
        };

        if (expected is { } status)
            Assert.True(status == response.StatusCode, $"{endpointKey} as {actor}: expected {(int)status}, got {(int)response.StatusCode}");
        else
            Assert.True(response.IsSuccessStatusCode, $"{endpointKey} as {actor}: expected success, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public void EveryBoardEndpoint_AppearsInTheMatrix()
    {
        var actual = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(d => typeof(BoardControllerBase).IsAssignableFrom(d.ControllerTypeInfo))
            .Select(d => $"{d.ActionConstraints!.OfType<HttpMethodActionConstraint>().First().HttpMethods.First()} {d.AttributeRouteInfo!.Template}")
            .Order()
            .ToList();

        Assert.Equal(actual, Endpoints.Select(e => e.Key).Order().ToList());
    }
}
