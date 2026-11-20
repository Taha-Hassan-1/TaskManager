using System.Text.Json.Serialization;
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
// Enums travel as names ("Owner", "High"), which is what the generated Angular client expects.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

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

// Lets the integration tests use WebApplicationFactory<Program>.
public partial class Program;
