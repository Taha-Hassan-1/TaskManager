using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamBoard.API.Data;
using TeamBoard.API.Infrastructure;
using TeamBoard.API.Models;

namespace TeamBoard.API.Boards;

public sealed class MembersController(AppDbContext db, IAuthorizationService authorization, IBoardEvents events)
    : BoardControllerBase(db, authorization, events)
{
    [HttpPost("boards/{boardId:int}/members")]
    public async Task<ActionResult<MemberDto>> Invite(int boardId, InviteMemberRequest request, CancellationToken ct)
    {
        if (await DenyUnlessAsync(boardId, BoardAccess.Owner) is { } denied) return denied;
        if (request.Role == BoardRole.Owner)
        {
            ModelState.AddModelError(nameof(request.Role), "A board has exactly one Owner; invite as Editor or Viewer.");
            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await Db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null) return NotFoundProblem("No user has that email address.");

        if (await Db.BoardMembers.AnyAsync(m => m.BoardId == boardId && m.UserId == user.Id, ct))
            return ConflictProblem("That user is already a member of this board.");

        Db.BoardMembers.Add(new BoardMember { BoardId = boardId, UserId = user.Id, Role = request.Role });
        await Db.SaveChangesAsync(ct);
        var dto = new MemberDto(user.Id, user.Username, user.Email, request.Role);
        await PublishAsync(boardId, ActivityType.MemberAdded, LiveMethods.MemberChanged,
            origin => new MemberChangedMessage(boardId, user.Id, dto, origin), ct,
            payload: new { userId = user.Id, username = user.Username, role = request.Role });

        return Created($"/api/boards/{boardId}", dto);
    }

    [HttpPut("boards/{boardId:int}/members/{userId:int}")]
    public async Task<ActionResult<MemberDto>> ChangeRole(
        int boardId, int userId, ChangeMemberRoleRequest request, CancellationToken ct)
    {
        if (await DenyUnlessAsync(boardId, BoardAccess.Owner) is { } denied) return denied;
        if (request.Role == BoardRole.Owner)
        {
            ModelState.AddModelError(nameof(request.Role), "A board has exactly one Owner; choose Editor or Viewer.");
            return ValidationProblem(ModelState);
        }

        var member = await Db.BoardMembers.Include(m => m.User)
            .SingleOrDefaultAsync(m => m.BoardId == boardId && m.UserId == userId, ct);
        if (member is null) return NotFoundProblem("That user is not a member of this board.");
        if (member.Role == BoardRole.Owner) return ConflictProblem("The owner's role cannot be changed.");

        member.Role = request.Role;
        await Db.SaveChangesAsync(ct);
        var dto = new MemberDto(userId, member.User.Username, member.User.Email, member.Role);
        await PublishAsync(boardId, ActivityType.MemberRoleChanged, LiveMethods.MemberChanged,
            origin => new MemberChangedMessage(boardId, userId, dto, origin), ct,
            payload: new { userId, username = member.User.Username, role = request.Role });

        return Ok(dto);
    }

    /// <summary>The owner removes anyone; any member may remove themselves (leave).</summary>
    [HttpDelete("boards/{boardId:int}/members/{userId:int}")]
    public async Task<IActionResult> Remove(int boardId, int userId, CancellationToken ct)
    {
        var requirement = userId == UserId ? BoardAccess.Viewer : BoardAccess.Owner;
        if (await DenyUnlessAsync(boardId, requirement) is { } denied) return denied;

        var target = await Db.BoardMembers.AsNoTracking()
            .Where(m => m.BoardId == boardId && m.UserId == userId)
            .Select(m => new { m.Role, m.User.Username })
            .SingleOrDefaultAsync(ct);
        if (target is null) return NotFoundProblem("That user is not a member of this board.");
        if (target.Role == BoardRole.Owner) return ConflictProblem("The owner cannot be removed from the board.");

        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        await Db.Tasks.Where(t => t.BoardId == boardId && t.AssigneeId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.AssigneeId, (int?)null), ct);
        await Db.BoardMembers.Where(m => m.BoardId == boardId && m.UserId == userId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);

        await PublishAsync(boardId, ActivityType.MemberRemoved, LiveMethods.MemberChanged,
            origin => new MemberChangedMessage(boardId, userId, null, origin), ct,
            payload: new { userId, username = target.Username });
        return NoContent();
    }
}
