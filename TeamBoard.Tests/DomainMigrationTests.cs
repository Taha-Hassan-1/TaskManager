using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TeamBoard.API.Data;

namespace TeamBoard.Tests;

/// <summary>
/// Runs the real migrations against a throwaway SQL Server database: build the old schema, insert
/// old-style rows, migrate forward, check the data step, then migrate back.
/// Server comes from TEST_SQL_CONNECTION (CI uses a service container); locally it defaults to SQL Express.
/// </summary>
public class DomainMigrationTests : IAsyncLifetime
{
    private const string OldSchema = "20260503204218_InitialCreate";
    private const string NewSchema = "20261120104031_DomainModel";

    private readonly string _dbName = "TeamBoardTest_" + Guid.NewGuid().ToString("N");
    private readonly string _server = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")
        ?? "Server=localhost\\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True";

    private string DbConnection => new SqlConnectionStringBuilder(_server) { InitialCatalog = _dbName }.ConnectionString;

    public async Task InitializeAsync() => await ExecuteAsync(_server, $"CREATE DATABASE [{_dbName}]");

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteAsync(_server, $"ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}]");
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(DbConnection).Options);

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var conn = new SqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<Dictionary<string, object?>>> QueryAsync(string sql)
    {
        await using var conn = new SqlConnection(DbConnection);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    // User 1 has three tasks (two open, one completed), user 2 has one, user 3 has none.
    private async Task SeedOldSchemaAsync()
    {
        await ExecuteAsync(DbConnection, @"
INSERT INTO Users (Username, Email, PasswordHash, CreatedAt) VALUES
  ('ann', 'ann@example.com', 'x', '2026-01-01'),
  ('bob', 'bob@example.com', 'x', '2026-01-01'),
  ('cy',  'cy@example.com',  'x', '2026-01-01');
DECLARE @ann int = (SELECT Id FROM Users WHERE Username = 'ann');
DECLARE @bob int = (SELECT Id FROM Users WHERE Username = 'bob');
INSERT INTO Tasks (Title, Description, IsCompleted, DueDate, CreatedAt, UserId) VALUES
  ('second open', NULL,  0, NULL,         '2026-03-02', @ann),
  ('first open',  'desc', 0, '2026-12-31', '2026-03-01', @ann),
  ('finished',    NULL,  1, NULL,         '2026-02-01', @ann),
  ('bobs task',   NULL,  0, NULL,         '2026-03-05', @bob);");
    }

    [Fact]
    public async Task OldTasks_MoveIntoAPersonalBoardPerUser()
    {
        await using (var db = CreateContext())
            await db.GetService<IMigrator>().MigrateAsync(OldSchema);
        await SeedOldSchemaAsync();

        await using (var db = CreateContext())
            await db.GetService<IMigrator>().MigrateAsync(NewSchema);

        // One board per user who had tasks; the user without tasks gets none.
        var boards = await QueryAsync(
            "SELECT b.Name, u.Username FROM Boards b JOIN Users u ON u.Id = b.OwnerId ORDER BY u.Username");
        Assert.Equal(["ann", "bob"], boards.Select(b => (string)b["Username"]!));
        Assert.All(boards, b => Assert.Equal("My tasks", b["Name"]));

        // The owner is a member with the Owner role, and each board has the three default columns.
        var members = await QueryAsync("SELECT Role FROM BoardMembers");
        Assert.Equal(2, members.Count);
        Assert.All(members, m => Assert.Equal("Owner", m["Role"]));
        var columns = await QueryAsync(
            "SELECT c.Name FROM Columns c JOIN Boards b ON b.Id = c.BoardId JOIN Users u ON u.Id = b.OwnerId " +
            "WHERE u.Username = 'ann' ORDER BY c.Position");
        Assert.Equal(["To do", "Doing", "Done"], columns.Select(c => (string)c["Name"]!));

        // Completed task lands in Done, open ones in To do, positions follow CreatedAt order.
        var tasks = await QueryAsync(@"
SELECT t.Title, c.Name AS ColumnName, t.Position, t.Priority, t.Description, u.Username, t.BoardId, b.OwnerId, t.CreatedById
FROM Tasks t
JOIN Columns c ON c.Id = t.ColumnId
JOIN Boards b ON b.Id = t.BoardId
JOIN Users u ON u.Id = t.CreatedById
ORDER BY t.Title");
        Assert.Equal(4, tasks.Count);
        Assert.All(tasks, t => Assert.Equal(t["OwnerId"], t["CreatedById"]));
        Assert.All(tasks, t => Assert.Equal(1, t["Priority"])); // Normal

        string ColumnOf(string title) => (string)tasks.Single(t => (string)t["Title"]! == title)["ColumnName"]!;
        double PositionOf(string title) => (double)tasks.Single(t => (string)t["Title"]! == title)["Position"]!;
        Assert.Equal("Done", ColumnOf("finished"));
        Assert.Equal("To do", ColumnOf("first open"));
        Assert.Equal("To do", ColumnOf("second open"));
        Assert.Equal("To do", ColumnOf("bobs task"));
        Assert.True(PositionOf("first open") < PositionOf("second open"));
        Assert.Equal("desc", tasks.Single(t => (string)t["Title"]! == "first open")["Description"]);
    }

    [Fact]
    public async Task Migration_CanBeRolledBack_AndRestoresIsCompleted()
    {
        await using (var db = CreateContext())
            await db.GetService<IMigrator>().MigrateAsync(OldSchema);
        await SeedOldSchemaAsync();
        await using (var db = CreateContext())
            await db.GetService<IMigrator>().MigrateAsync(NewSchema);

        await using (var db = CreateContext())
            await db.GetService<IMigrator>().MigrateAsync(OldSchema);

        var tasks = await QueryAsync("SELECT Title, IsCompleted, UserId FROM Tasks ORDER BY Title");
        Assert.Equal(4, tasks.Count);
        Assert.True((bool)tasks.Single(t => (string)t["Title"]! == "finished")["IsCompleted"]!);
        Assert.False((bool)tasks.Single(t => (string)t["Title"]! == "first open")["IsCompleted"]!);
        Assert.Empty(await QueryAsync("SELECT 1 AS X FROM sys.tables WHERE name = 'Boards'"));
    }

    [Fact]
    public async Task FreshDatabase_AllowsOnlyOneOwnerPerBoard()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await ExecuteAsync(DbConnection, @"
INSERT INTO Users (Username, Email, PasswordHash, CreatedAt) VALUES ('a','a@x.com','x',SYSUTCDATETIME()), ('b','b@x.com','x',SYSUTCDATETIME());
INSERT INTO Boards (Name, OwnerId, CreatedAt) VALUES ('B', (SELECT Id FROM Users WHERE Username='a'), SYSUTCDATETIME());
INSERT INTO BoardMembers (BoardId, UserId, Role) VALUES ((SELECT MAX(Id) FROM Boards), (SELECT Id FROM Users WHERE Username='a'), 'Owner');");

        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(DbConnection,
            "INSERT INTO BoardMembers (BoardId, UserId, Role) VALUES ((SELECT MAX(Id) FROM Boards), (SELECT Id FROM Users WHERE Username='b'), 'Owner')"));
    }
}
