using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using TeamBoard.API.Auth;
using TeamBoard.API.Data;
using TeamBoard.API.Infrastructure;
using TeamBoard.API.Realtime;

var builder = WebApplication.CreateBuilder(args);

// Structured logs: readable lines in development, one JSON object per line everywhere else (for a log service).
// Every line of a request carries its CorrelationId, which is also in the response header and in ProblemDetails.
builder.Host.UseSerilog((context, _, logger) =>
{
    logger.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext();
    if (context.HostingEnvironment.IsDevelopment())
        logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}");
    else
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
});

// Dev: dotnet user-secrets. Prod: environment variables (ConnectionStrings__DefaultConnection, Jwt__Key).
// Missing or invalid settings fail at boot, not on the first request.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddTeamBoardAuth(builder.Configuration);
builder.Services.AddTeamBoardBoards();
builder.Services.AddWebHardening(builder.Configuration);

// /health/live: the process is up. /health/ready: and it can reach its database.
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

// One media type in and out keeps the OpenAPI document, and so the generated client, small and unambiguous.
builder.Services.AddControllers(o =>
    {
        o.Filters.Add(new ProducesAttribute("application/json"));
        // No global [Consumes] filter: it makes routing drop every controller for a request without a Content-Type (any GET)
        // as soon as another endpoint competes for the path, such as the SPA fallback. OpenApiJsonOnly keeps the document to JSON instead.
    })
    .AddJsonOptions(o => JsonConfig.Apply(o.JsonSerializerOptions));
// The OpenAPI generator reads these options, not MVC's, so both must agree for the document to match the wire format.
builder.Services.ConfigureHttpJsonOptions(o => JsonConfig.Apply(o.SerializerOptions));
builder.Services.AddOpenApi(o => o.UseJsonOnly());

var app = builder.Build();

// `dotnet TeamBoard.API.dll --migrate` applies the migrations and exits: the explicit step production uses.
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    app.Logger.LogInformation("Database migrated.");
    return;
}

// Development convenience, switched on by the launch profiles only.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseWebHardeningFirst(); // proxy headers, correlation id, exception handler, HTTPS, security headers, compression
app.UseSerilogRequestLogging(o =>
    o.GetLevel = (context, _, ex) =>
        ex is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
        : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
        : LogEventLevel.Information);
app.UseSpaFiles(); // the built Angular app's files, when there is one

// Routing is placed explicitly, after the static files. Left to itself ASP.NET puts it first, the SPA fallback then
// matches every path, and the static file middleware (which stands aside for a matched endpoint) never serves a file.
app.UseRouting();

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
app.MapHub<BoardHub>("/hubs/board");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapSpaFallback(); // last: only what nothing else matched
app.Run();

// Lets the integration tests use WebApplicationFactory<Program>.
public partial class Program;
