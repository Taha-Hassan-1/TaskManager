using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TeamBoard.API.Data;
using TeamBoard.API.Models;

namespace TeamBoard.API.Infrastructure;

/// <summary>"The caller holds at least this role on the board." Roles are ordered Viewer &lt; Editor &lt; Owner.</summary>
public sealed class BoardAccessRequirement(BoardRole minimum) : IAuthorizationRequirement
{
    public BoardRole Minimum { get; } = minimum;
}

/// <summary>The only way controllers state who may do what; no role strings anywhere.</summary>
public static class BoardAccess
{
    public static readonly BoardAccessRequirement Viewer = new(BoardRole.Viewer); // any member
    public static readonly BoardAccessRequirement Editor = new(BoardRole.Editor);
    public static readonly BoardAccessRequirement Owner = new(BoardRole.Owner);

    public const string NotMember = "not-member";
    public const string InsufficientRole = "insufficient-role";
}

/// <summary>One place that decides board permissions. The resource is the board id.</summary>
public sealed class BoardAccessHandler(AppDbContext db) : AuthorizationHandler<BoardAccessRequirement, int>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, BoardAccessRequirement requirement, int boardId)
    {
        if (!int.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
        {
            context.Fail();
            return;
        }

        var role = await db.BoardMembers.AsNoTracking()
            .Where(m => m.BoardId == boardId && m.UserId == userId)
            .Select(m => (BoardRole?)m.Role)
            .SingleOrDefaultAsync();

        if (role is null)
            context.Fail(new AuthorizationFailureReason(this, BoardAccess.NotMember));
        else if (role.Value >= requirement.Minimum)
            context.Succeed(requirement);
        else
            context.Fail(new AuthorizationFailureReason(this, BoardAccess.InsufficientRole));
    }
}
