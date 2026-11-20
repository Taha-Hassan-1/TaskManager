using Microsoft.AspNetCore.Authorization;
using TaskManager.API.Realtime;

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
        services.AddScoped<IBoardEventHandler, SignalRNotifier>();

        // Live updates: the hub only pushes; see Realtime/.
        services.AddSingleton<BoardConnections>();
        services.AddSingleton<IBoardBroadcaster, SignalRBoardBroadcaster>();
        services.AddSignalR().AddJsonProtocol(o => JsonConfig.Apply(o.PayloadSerializerOptions));
        return services;
    }
}
