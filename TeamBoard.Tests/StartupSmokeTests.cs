using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace TeamBoard.Tests;

public class StartupSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestKey = "test-only-key-0123456789-0123456789-0123456789";
    private readonly WebApplicationFactory<Program> _factory;

    public StartupSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Jwt:Key", TestKey)
            .UseSetting("ConnectionStrings:DefaultConnection", "Server=(local);Database=unused;Trusted_Connection=True"));
    }

    [Fact]
    public async Task OpenApiDocument_IsServed()
    {
        var response = await _factory.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Startup_FailsWhenJwtKeyIsMissing()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("Jwt:Key", "")
            .UseSetting("ConnectionStrings:DefaultConnection", "Server=(local);Database=unused"));
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }
}
