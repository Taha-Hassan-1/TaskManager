using Microsoft.AspNetCore.Authorization;

namespace TaskManager.API.Infrastructure;

public static class BoardServiceCollectionExtensions
{
    public static IServiceCollection AddTeamBoardBoards(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, BoardAccessHandler>();
        services.AddSingleton<IPositionCalculator, GapPositionCalculator>();

        // Observer: add a listener (SignalR in M5) with one more line here.
        services.AddScoped<IBoardEvents, BoardEvents>();
        services.AddScoped<IBoardEventHandler, ActivityLogHandler>();
        return services;
    }
}
