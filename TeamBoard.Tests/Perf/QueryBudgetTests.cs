using TeamBoard.API.Tasks;

namespace TeamBoard.Tests.Perf;

/// <summary>
/// The hot paths have a fixed number of SQL statements, however big the board is. These fail if a change makes
/// a request chatty (the classic N+1), long before anyone has to notice it as slowness. The numbers are the
/// ones recorded in docs/PERFORMANCE.md.
/// </summary>
public class QueryBudgetTests : IClassFixture<CapturingApiFactory>
{
    private readonly CapturingApiFactory _factory;
    private readonly HttpClient _http;

    public QueryBudgetTests(CapturingApiFactory factory)
    {
        _factory = factory;
        _http = factory.CreateApiClient();
    }

    [Fact]
    public async Task LoadingABoard_IsSevenStatements_WhateverItsSize()
    {
        var small = await BoardTestKit.SeedBoardAsync(_factory);
        var big = await PerfKit.SeedBigBoardAsync(_factory);

        _factory.Sql.Reset();
        (await _http.SendAsync(HttpMethod.Get, $"/api/boards/{small.BoardId}", small.Owner.Token)).EnsureSuccessStatusCode();
        var forSmall = _factory.Sql.Count;

        _factory.Sql.Reset();
        (await _http.SendAsync(HttpMethod.Get, $"/api/boards/{big.BoardId}", big.Owner.Token)).EnsureSuccessStatusCode();
        var forBig = _factory.Sql.Count;

        Assert.Equal(7, forSmall);
        Assert.Equal(forSmall, forBig); // 2 cards or 500: the same seven statements
    }

    [Fact]
    public async Task MovingACard_IsSixStatements_AndWritesOneCardRow()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        _factory.Sql.Reset();
        (await _http.SendAsync(HttpMethod.Patch, $"/api/tasks/{s.CardId}/move", s.Editor.Token, new { columnId = s.DoingId, rowVersion = s.CardRowVersion })).EnsureSuccessStatusCode();

        var statements = _factory.Sql.Entries;
        Assert.Equal(6, statements.Count);
        Assert.Single(statements, e => e.Sql.Contains("UPDATE [Tasks]")); // one card row, not the whole column
    }

    [Fact]
    public async Task AnActivityPage_IsTwoStatements_AndNeverCountsOrSkipsRows()
    {
        var s = await BoardTestKit.SeedBoardAsync(_factory);

        _factory.Sql.Reset();
        (await _http.SendAsync(HttpMethod.Get, $"/api/boards/{s.BoardId}/activity", s.Viewer.Token)).EnsureSuccessStatusCode();

        Assert.Equal(2, _factory.Sql.Count); // the permission check and the page itself
        Assert.DoesNotContain(_factory.Sql.Entries, e => e.Sql.Contains("COUNT(") || e.Sql.Contains("OFFSET"));
    }
}
