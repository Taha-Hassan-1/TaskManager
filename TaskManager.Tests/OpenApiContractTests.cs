using System.Text.Json;
using System.Text.Json.Nodes;

namespace TaskManager.Tests;

/// <summary>
/// client/openapi.json is the contract the Angular client is generated from. This test fails when the API
/// has changed and the file has not: run <c>npm run api:update</c> in client/ to refresh it.
/// </summary>
public class OpenApiContractTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public OpenApiContractTests(ApiFactory factory) => _factory = factory;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaskManager.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find TaskManager.slnx above the test output.");
    }

    private static string Normalize(string json)
    {
        // Pretty-printed with LF endings so the file diffs well in git and is the same on every OS.
        var node = JsonNode.Parse(json)!;
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
    }

    [Fact]
    public async Task CommittedOpenApiDocument_MatchesTheApi()
    {
        var response = await _factory.CreateApiClient().GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var current = Normalize(await response.Content.ReadAsStringAsync());

        var path = Path.Combine(RepoRoot(), "client", "openapi.json");
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(path, current);
            return;
        }

        Assert.True(File.Exists(path), "client/openapi.json is missing. Run `npm run api:update` in client/.");
        var committed = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
        Assert.True(committed == current, "client/openapi.json is out of date. Run `npm run api:update` in client/.");
    }
}
