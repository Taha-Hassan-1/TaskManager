using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace TaskManager.API.Auth;

public static class AuthServiceCollectionExtensions
{
    public const string RateLimitPolicy = "auth";

    public static IServiceCollection AddTeamBoardAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RefreshOptions>().Bind(config.GetSection(RefreshOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RateLimitOptions>().Bind(config.GetSection(RateLimitOptions.Section)).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<AuthService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        // Configured from JwtOptions so the signing key and the validated issuer/audience have one source.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var o = jwt.Value;
                // Keep claim names as issued ("sub", "name") instead of mapping them to long WS-* URIs.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidIssuer = o.Issuer,
                    ValidateAudience = true,
                    ValidAudience = o.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "name"
                };
            });
        services.AddAuthorization();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Per client IP and per endpoint, so a burst of logins does not lock out token refresh.
            // Behind a reverse proxy the forwarded client IP must be configured (M6).
            limiter.AddPolicy(RateLimitPolicy, context =>
            {
                var permit = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.AuthPermitPerMinute;
                var key = $"{context.Request.Path}|{context.Connection.RemoteIpAddress}";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });
            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests." }, ct);
            };
        });

        return services;
    }
}
