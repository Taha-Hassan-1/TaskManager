using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.API.Data;

namespace TaskManager.Tests;

/// <summary>
/// The real API on a test server with its own migrated SQL Server database, dropped afterwards.
/// The server comes from TEST_SQL_CONNECTION (CI) or defaults to local SQL Express.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string JwtKey = "test-only-key-0123456789-0123456789-0123456789";

    private static readonly string SqlServer = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")
        ?? "Server=localhost\\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True";

    private readonly string _dbName = "TaskManagerApi_" + Guid.NewGuid().ToString("N");

    public string ConnectionString => new SqlConnectionStringBuilder(SqlServer) { InitialCatalog = _dbName }.ConnectionString;

    /// <summary>Requests per minute per endpoint for the auth rate limiter. High by default so tests never trip it.</summary>
    protected virtual int AuthPermitPerMinute => 1000;

    protected virtual string EnvironmentName => "Development";

    /// <summary>Extra configuration for a test that needs the app set up differently (proxy headers, SPA folder...).</summary>
    protected virtual IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        foreach (var (key, value) in ExtraSettings) builder.UseSetting(key, value);
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning"); // keep test output readable
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("RateLimit:AuthPermitPerMinute", AuthPermitPerMinute.ToString());
    }

    public async Task InitializeAsync()
    {
        await ExecuteAsync(SqlServer, $"CREATE DATABASE [{_dbName}]");
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Dispose();
        SqlConnection.ClearAllPools();
        await ExecuteAsync(SqlServer, $"ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}]");
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        // The refresh cookie is Secure, so talk https; cookies are handled by hand in the tests.
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
        AllowAutoRedirect = false
    });

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var conn = new SqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

public sealed class RateLimitedApiFactory : ApiFactory
{
    protected override int AuthPermitPerMinute => 3;
}
