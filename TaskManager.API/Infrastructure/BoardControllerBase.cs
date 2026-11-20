using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManager.API.Data;

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

    protected ObjectResult NotFoundProblem(string title = "Not found.") =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: title);

    protected ObjectResult ConflictProblem(string title, string? detail = null) =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: title, detail: detail);
}
