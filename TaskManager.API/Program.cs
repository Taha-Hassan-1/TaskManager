using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;
using Microsoft.EntityFrameworkCore;
using TaskManager.API.Auth;
using TaskManager.API.Data;
using TaskManager.API.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Dev: dotnet user-secrets. Prod: environment variables (ConnectionStrings__DefaultConnection, Jwt__Key).
// Missing or invalid settings fail at boot, not on the first request.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddTeamBoardAuth(builder.Configuration);
builder.Services.AddTeamBoardBoards();

builder.Services.AddProblemDetails();
// One media type in and out keeps the OpenAPI document, and so the generated client, small and unambiguous.
builder.Services.AddControllers(o =>
    {
        o.Filters.Add(new ProducesAttribute("application/json"));
        o.Filters.Add(new ConsumesAttribute("application/json"));
    })
    .AddJsonOptions(o => ConfigureJson(o.JsonSerializerOptions));
// The OpenAPI generator reads these options, not MVC's, so both must agree for the document to match the wire format.
builder.Services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));
builder.Services.AddOpenApi(o => o.UseJsonOnly());

var app = builder.Build();

// Development convenience, switched on by the launch profiles only. Production applies migrations as an explicit step.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // dev-only explorer at /scalar
}

app.UseStatusCodePages(); // 401/403/404 bodies as ProblemDetails
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.Run();

// Enums travel as names ("Owner", "High") and numbers only as numbers, which is what the generated Angular client expects.
static void ConfigureJson(System.Text.Json.JsonSerializerOptions o)
{
    o.Converters.Add(new JsonStringEnumConverter());
    o.NumberHandling = JsonNumberHandling.Strict;
}

// Lets the integration tests use WebApplicationFactory<Program>.
public partial class Program;
