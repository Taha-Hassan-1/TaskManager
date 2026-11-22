using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using Xunit.Abstractions;
using TeamBoard.API.Boards;
using TeamBoard.API.Tasks;

namespace TeamBoard.Tests.Perf;

/// <summary>
/// Measurements for docs/PERFORMANCE.md. Run with PERF=1; the raw output is also appended to docs/perf-raw.txt.
/// The server is in-process (no network), the database is a real SQL Server on this machine.
/// </summary>
public class PerformanceTests : IClassFixture<PerfApiFactory>
{
    private readonly PerfApiFactory _factory;
    private readonly ITestOutputHelper _output;
    private readonly HttpClient _http;
    private readonly StringBuilder _report = new();

    public PerformanceTests(PerfApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
        _http = factory.CreateApiClient();
    }

    private void Line(string text)
    {
        _output.WriteLine(text);
        _report.AppendLine(text);
    }

    private void Save(string title)
    {
        var dir = DirectoryOf("TeamBoard.slnx");
        var path = Path.Combine(dir, "docs", "perf-raw.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, $"=== {title} · {DateTime.Now:yyyy-MM-dd HH:mm} ===\n{_report}\n");
    }

    private static string DirectoryOf(string marker)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, marker))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static long Gzipped(byte[] bytes)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(bytes);
        return buffer.Length;
    }

    private void DescribeSql(string label, IReadOnlyList<SqlCapture.Entry> entries)
    {
        Line($"  {label}: {entries.Count} SQL statements, {entries.Sum(e => e.Milliseconds):F1} ms in the database");
        foreach (var e in entries)
        {
            var flat = string.Join(" ", e.Sql.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
            Line($"    [{e.Milliseconds,7:F1} ms] {flat[..Math.Min(230, flat.Length)]}");
        }
    }

    [PerfFact]
    public async Task BoardLoad_500Cards_NaiveGraphVersusFlatProjections()
    {
        var big = await PerfKit.SeedBigBoardAsync(_factory);
        var token = big.Owner.Token;

        async Task<byte[]> Get(string url)
        {
            var response = await _http.SendAsync(HttpMethod.Get, url, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        Line("Board load: 500 cards, 5 columns, 8 labels, 300 comments, 50 members");
        foreach (var (name, url, warmup, runs) in new[]
        {
            // The naive query returns hundreds of thousands of rows per request, so it gets far fewer runs.
            ("before (entity graph, tracked)", $"/api/naive/boards/{big.BoardId}", 1, 12),
            ("after  (flat projections, no tracking)", $"/api/boards/{big.BoardId}", 20, 200),
        })
        {
            _factory.Sql.Reset();
            var body = await Get(url);
            var sql = _factory.Sql.Entries;

            var samples = await PerfKit.MeasureAsync(() => Get(url), warmup, runs);
            Line($"{name}");
            Line($"  latency (sequential, in-process): {PerfKit.Summary(samples)}");
            Line($"  response: {body.Length:N0} bytes raw, {Gzipped(body):N0} bytes gzip");
            DescribeSql("one request", sql);
        }

        var after = await PerfKit.MeasureAsync(() => Get($"/api/boards/{big.BoardId}"), 10, 200);
        Save("board load");
        Assert.True(PerfKit.Percentile(after, 95) < 200, $"p95 {PerfKit.Percentile(after, 95):F1} ms is not under the 200 ms target");
    }

    [PerfFact]
    public async Task Move_StatementsAndLatency()
    {
        var big = await PerfKit.SeedBigBoardAsync(_factory);
        var cardId = big.CardIds[0];
        var detail = await (await _http.SendAsync(HttpMethod.Get, $"/api/tasks/{cardId}", big.Owner.Token)).ReadAsync<CardDetailDto>();
        var version = detail.Card.RowVersion;
        var column = 0;

        async Task Move()
        {
            column = (column + 1) % 5;
            var response = await _http.SendAsync(HttpMethod.Patch, $"/api/tasks/{cardId}/move", big.Owner.Token, new { columnId = big.ColumnIds[column], rowVersion = version });
            response.EnsureSuccessStatusCode();
            version = (await response.ReadAsync<MoveResultDto>()).RowVersion;
        }

        _factory.Sql.Reset();
        await Move();
        Line("Move one card to the bottom of another column (500-card board)");
        DescribeSql("one move", _factory.Sql.Entries);

        var samples = await PerfKit.MeasureAsync(Move, warmup: 10, runs: 150);
        Line($"  latency (sequential, in-process): {PerfKit.Summary(samples)}");
        Save("move");
    }

    [PerfFact]
    public async Task ActivityFeed_PageOverFiveThousandEntries()
    {
        var big = await PerfKit.SeedBigBoardAsync(_factory);
        await _factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(@"
WITH n AS (SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT INTO Activity (BoardId, ActorId, [Type], TaskItemId, Payload, CreatedAt)
SELECT {0}, {1}, 'TaskMoved', NULL, '{{""title"":""x""}}', DATEADD(second, i, '2026-01-01') FROM n", big.BoardId, big.Owner.Id);
            return 0;
        });

        var url = $"/api/boards/{big.BoardId}/activity?limit=30";
        _factory.Sql.Reset();
        var first = await (await _http.SendAsync(HttpMethod.Get, url, big.Owner.Token)).ReadAsync<API.Activity.ActivityPageDto>();
        Line("Activity feed: first page of 30 from 5,000 entries");
        DescribeSql("first page", _factory.Sql.Entries);
        Line($"  latency: {PerfKit.Summary(await PerfKit.MeasureAsync(async () => (await _http.SendAsync(HttpMethod.Get, url, big.Owner.Token)).EnsureSuccessStatusCode(), 10, 200))}");

        // A page deep in the history costs the same as the first: the cursor seeks, it does not skip.
        var deep = first;
        for (var i = 0; i < 100; i++)
            deep = await (await _http.SendAsync(HttpMethod.Get, url + $"&cursor={deep.NextCursor}", big.Owner.Token)).ReadAsync<API.Activity.ActivityPageDto>();
        var deepUrl = url + $"&cursor={deep.NextCursor}";
        Line($"  page 102 (cursor): {PerfKit.Summary(await PerfKit.MeasureAsync(async () => (await _http.SendAsync(HttpMethod.Get, deepUrl, big.Owner.Token)).EnsureSuccessStatusCode(), 10, 200))}");
        Save("activity feed");
    }

    [PerfFact]
    public async Task LiveMove_FanOutToFiftyClients()
    {
        var big = await PerfKit.SeedBigBoardAsync(_factory);
        var clients = new List<HubTestClient>();
        foreach (var member in big.Members)
        {
            var client = new HubTestClient(_factory, member.Token);
            await client.StartAsync();
            await client.JoinAsync(big.BoardId);
            clients.Add(client);
        }

        var cardId = big.CardIds[0];
        var version = (await (await _http.SendAsync(HttpMethod.Get, $"/api/tasks/{cardId}", big.Owner.Token)).ReadAsync<CardDetailDto>()).Card.RowVersion;
        var column = 0;
        var samples = new List<double>();
        var messageBytes = 0;

        for (var run = 0; run < 25; run++)
        {
            column = (column + 1) % 5;
            var started = System.Diagnostics.Stopwatch.StartNew();
            var response = await _http.SendAsync(HttpMethod.Patch, $"/api/tasks/{cardId}/move", big.Owner.Token, new { columnId = big.ColumnIds[column], rowVersion = version });
            var moved = await response.ReadAsync<MoveResultDto>();
            version = moved.RowVersion;
            var expected = Convert.ToBase64String(moved.RowVersion);

            var arrivals = await Task.WhenAll(clients.Select(c => c.WaitForAsync("TaskMoved", b => b.GetProperty("rowVersion").GetString() == expected, pollMs: 1)));
            samples.Add(started.Elapsed.TotalMilliseconds);
            messageBytes = Encoding.UTF8.GetByteCount(arrivals[0].Body.GetRawText());
        }

        var board = await (await _http.SendAsync(HttpMethod.Get, $"/api/boards/{big.BoardId}", big.Owner.Token)).Content.ReadAsByteArrayAsync();
        Line("Live move to 50 connected clients (long polling over the in-memory test server, so a worst case)");
        Line($"  move accepted until the last of 50 clients has it: {PerfKit.Summary(samples)}");
        Line($"  pushed message: {messageBytes} bytes; the whole board would be {board.Length:N0} bytes ({board.Length / (double)messageBytes:F0}x)");
        Save("fan-out");

        foreach (var client in clients) await client.DisposeAsync();
    }
}
