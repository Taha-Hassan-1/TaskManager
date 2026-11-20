using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManager.API.Data;
using TaskManager.API.Models;

namespace TaskManager.API.Infrastructure;

/// <summary>Shared plumbing for every signed-in, board-scoped controller.</summary>
[ApiController]
[Authorize]
[Route("api")]
public abstract class BoardControllerBase(AppDbContext db, IAuthorizationService authorization, IBoardEvents events)
    : ControllerBase
{
    protected AppDbContext Db { get; } = db;
    protected IBoardEvents Events { get; } = events;

    // [Authorize] guarantees a validated token, whose subject is the user id.
    protected int UserId => int.Parse(base.User.FindFirst("sub")!.Value);

    private string ActorName => base.User.FindFirst("name")?.Value ?? "";

    /// <summary>
    /// Which browser tab sent this request. The live messages echo it back so that tab can ignore the
    /// echo of its own change. Not a secret and not trusted for anything but that.
    /// </summary>
    private string? OriginClientId =>
        Request.Headers["X-Client-Id"].FirstOrDefault() is { Length: > 0 and <= 64 } id ? id : null;

    /// <summary>
    /// Null when the caller may proceed. Otherwise the response to return: 404 for someone who is not a member
    /// (so board ids cannot be probed), 403 for a member whose role is too low.
    /// </summary>
    protected async Task<ActionResult?> DenyUnlessAsync(int boardId, BoardAccessRequirement requirement)
    {
        var result = await authorization.AuthorizeAsync(base.User, boardId, requirement);
        if (result.Succeeded) return null;

        var notMember = result.Failure?.FailureReasons.Any(r => r.Message == BoardAccess.NotMember) ?? true;
        return notMember
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found.")
            : Problem(statusCode: StatusCodes.Status403Forbidden, title: "You do not have permission to do that on this board.");
    }

    /// <summary>
    /// Announces a committed change: to the activity feed (when <paramref name="type"/> is given) and to everyone
    /// watching the board. <paramref name="liveBody"/> builds the pushed message once the origin client is known.
    /// </summary>
    protected Task PublishAsync(
        int boardId, ActivityType? type, string liveMethod, Func<string?, object> liveBody, CancellationToken ct,
        int? taskId = null, object? payload = null) =>
        Events.PublishAsync(
            new BoardEvent(boardId, UserId, ActorName, type, taskId, payload, new LiveMessage(liveMethod, liveBody(OriginClientId)), OriginClientId),
            ct);

    protected ObjectResult NotFoundProblem(string title = "Not found.") =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: title);

    protected ObjectResult ConflictProblem(string title, string? detail = null) =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: title, detail: detail);
}
