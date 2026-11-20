# TeamBoard (TaskManager)

A real-time collaborative kanban app: ASP.NET Core 10 Web API (JWT, SignalR, EF Core / SQL Server) with an Angular front end.

> Work in progress. See the spec and implementation plan for scope and milestones. Status: M0 (clean-up and baseline).

## Run the API

```powershell
cd TaskManager.API
dotnet user-secrets set "Jwt:Key" "<at least 32 random bytes>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<SQL Server connection string>"
dotnet run
```

In production the same values come from the environment variables `Jwt__Key` and `ConnectionStrings__DefaultConnection`.

## Tests

```powershell
dotnet test TaskManager.slnx
```
