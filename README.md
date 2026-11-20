# TeamBoard (TaskManager)

A real-time collaborative kanban app: ASP.NET Core 10 Web API (JWT, SignalR, EF Core / SQL Server) with an Angular front end.

> Work in progress. See the spec and implementation plan for scope and milestones. Status: M4 done (Angular app); next is M5 (real time).

## Run it

You need the .NET 10 SDK, Node 22.12 or newer, and a SQL Server (SQL Express is fine).

```powershell
# API: dotnet run applies the migrations to the configured database in Development
cd TaskManager.API
dotnet user-secrets set "Jwt:Key" "<at least 32 random bytes>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<SQL Server connection string>"
dotnet run --launch-profile http          # http://localhost:5020, API explorer at /scalar

# Angular app (second terminal): proxies /api to the API, so there is no CORS to set up
cd client
npm install
npm start                                 # http://localhost:4200
```

In production the same API settings come from the environment variables `Jwt__Key` and `ConnectionStrings__DefaultConnection`.

## Tests

```powershell
dotnet test TaskManager.slnx              # API, permissions, migrations (needs SQL Server)
cd client; npm test -- --no-watch         # Angular unit tests
```

## API contract and the generated client

`client/openapi.json` is the contract; the TypeScript client in `client/src/app/api` is generated from it and is not edited by hand.
After changing the API, run `npm run api:update` in `client/`. A backend test fails if `openapi.json` is stale, and CI fails if the generated client is.
