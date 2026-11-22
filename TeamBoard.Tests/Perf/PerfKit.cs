using Microsoft.AspNetCore.TestHost;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TeamBoard.API.Auth;
using TeamBoard.API.Boards;
using TeamBoard.API.Data;
using TeamBoard.API.Infrastructure;
using TeamBoard.API.Models;

namespace TeamBoard.Tests.Perf;

/// <summary>Records every SQL command the API runs, so a request's statement count and SQL can be asserted and reported.</summary>
public sealed class SqlCapture : DbCommandInterceptor
{
    public sealed record Entry(string Sql, double Milliseconds);

    private readonly ConcurrentQueue<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries.ToArray();
    public int Count => _entries.Count;
    public void Reset() => _entries.Clear();

    private void Add(DbCommand command, CommandExecutedEventData data) => _entries.Enqueue(new Entry(command.CommandText, data.Duration.TotalMilliseconds));

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken ct = default)
    {
        Add(command, eventData);
        return new ValueTask<DbDataReader>(result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken ct = default)
    {
        Add(command, eventData);
        return new ValueTask<int>(result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken ct = default)
    {
        Add(command, eventData);
        return new ValueTask<object?>(result);
    }
}

/// <summary>The API with SQL capture switched on.</summary>
public class CapturingApiFactory : ApiFactory
{
    public SqlCapture Sql { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(Sql);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(Sql));
        });
    }
}

/// <summary>Capturing, plus a deliberately naive board endpoint to measure the real one against.</summary>
public sealed class PerfApiFactory : CapturingApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddControllers().AddApplicationPart(typeof(NaiveBoardController).Assembly));
    }
}

/// <summary>
/// The board load as a first draft would write it: load the whole entity graph with Include, tracked, in one
/// query, then map. Same response as the real endpoint; here only to show what the real one improves on.
/// </summary>
public sealed class NaiveBoardController(AppDbContext db, IAuthorizationService authorization, IBoardEvents events)
    : BoardControllerBase(db, authorization, events)
{
    [HttpGet("naive/boards/{id:int}")]
    public async Task<ActionResult<BoardDetailDto>> Get(int id, CancellationToken ct)
    {
        if (await DenyUnlessAsync(id, BoardAccess.Viewer) is { } denied) return denied;

        var board = await Db.Boards
            .Include(b => b.Members).ThenInclude(m => m.User)
            .Include(b => b.Labels)
            .Include(b => b.Columns).ThenInclude(c => c.Tasks).ThenInclude(t => t.Labels)
            .Include(b => b.Columns).ThenInclude(c => c.Tasks).ThenInclude(t => t.Comments)
            .SingleAsync(b => b.Id == id, ct);

        var me = UserId;
        return Ok(new BoardDetailDto(
            board.Id, board.Name, board.OwnerId, board.Members.Single(m => m.UserId == me).Role,
            board.Members.Select(m => new MemberDto(m.UserId, m.User.Username, m.User.Email, m.Role)).ToList(),
            board.Columns.OrderBy(c => c.Position).Select(c => new ColumnDto(c.Id, c.Name, c.Position, c.WipLimit,
                c.Tasks.OrderBy(t => t.Position).Select(t => new CardDto(
                    t.Id, id, t.ColumnId, t.Title, t.Description, t.Position, t.Priority, t.DueDate, t.AssigneeId,
                    t.CreatedById, t.CreatedAt, t.Labels.Select(l => l.LabelId).ToList(), t.Comments.Count, t.RowVersion)).ToList())).ToList(),
            board.Labels.Select(l => new LabelDto(l.Id, l.Name, l.Color)).ToList()));
    }
}

public sealed record BigBoard(int BoardId, int[] ColumnIds, int[] CardIds, TestUser Owner, TestUser[] Members);

public static class PerfKit
{
    /// <summary>One board with 500 cards in 5 columns, 8 labels, 100 commented cards (300 comments) and 50 members.</summary>
    public static async Task<BigBoard> SeedBigBoardAsync(ApiFactory factory)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var tokens = factory.Services.GetRequiredService<ITokenService>();

        return await factory.WithDbAsync(async db =>
        {
            var users = Enumerable.Range(0, 50)
                .Select(i => new User { Username = $"perf{i}_{tag}", Email = $"perf{i}_{tag}@example.com", PasswordHash = "x" })
                .ToList();
            db.Users.AddRange(users);
            await db.SaveChangesAsync();

            var board = new Board { Name = "Big board", OwnerId = users[0].Id };
            board.Members.Add(new BoardMember { UserId = users[0].Id, Role = BoardRole.Owner });
            foreach (var u in users.Skip(1)) board.Members.Add(new BoardMember { UserId = u.Id, Role = BoardRole.Editor });
            for (var c = 0; c < 5; c++) board.Columns.Add(new BoardColumn { Name = $"Column {c + 1}", Position = (c + 1) * 1024 });
            for (var l = 0; l < 8; l++) board.Labels.Add(new Label { Name = $"label{l}", Color = "#336699" });
            db.Boards.Add(board);
            await db.SaveChangesAsync();

            var cards = new List<TaskItem>();
            for (var i = 0; i < 500; i++)
            {
                var column = board.Columns.ElementAt(i % 5);
                var card = new TaskItem
                {
                    BoardId = board.Id, ColumnId = column.Id, Title = $"Card number {i} with a realistic title", Description = "A few sentences of description, the kind a person writes. " + new string('x', 120),
                    Position = (i / 5 + 1) * 1024, CreatedById = users[0].Id, AssigneeId = users[i % users.Count].Id,
                    Priority = (TaskPriority)(i % 4), DueDate = i % 3 == 0 ? DateTime.UtcNow.AddDays(i % 30) : null,
                };
                foreach (var label in board.Labels.Skip(i % 6).Take(1 + i % 2)) card.Labels.Add(new TaskLabel { LabelId = label.Id });
                if (i % 5 == 0)
                    for (var k = 0; k < 3; k++) card.Comments.Add(new Comment { AuthorId = users[(i + k) % users.Count].Id, Body = "A comment about this card, of ordinary length." });
                cards.Add(card);
            }
            db.Tasks.AddRange(cards);
            await db.SaveChangesAsync();

            TestUser Wrap(User u) => new(u.Id, u.Username, u.Email, tokens.CreateAccessToken(u).Value);
            return new BigBoard(board.Id, board.Columns.Select(c => c.Id).ToArray(), cards.Select(c => c.Id).ToArray(), Wrap(users[0]), users.Select(Wrap).ToArray());
        });
    }

    public static async Task<List<double>> MeasureAsync(Func<Task> action, int warmup, int runs)
    {
        for (var i = 0; i < warmup; i++) await action();
        var samples = new List<double>(runs);
        for (var i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            await action();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        return samples;
    }

    public static double Percentile(IEnumerable<double> samples, double p)
    {
        var sorted = samples.Order().ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p / 100 * sorted.Count) - 1)];
    }

    public static string Summary(IReadOnlyCollection<double> samples) =>
        $"n={samples.Count}  p50={Percentile(samples, 50):F1} ms  p95={Percentile(samples, 95):F1} ms  p99={Percentile(samples, 99):F1} ms  max={samples.Max():F1} ms";
}

/// <summary>A measurement, not a test of behaviour: runs only with PERF=1 so the normal suite stays fast and steady.</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public PerfFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PERF") != "1") Skip = "Measurement. Run with PERF=1 to include it.";
    }
}
