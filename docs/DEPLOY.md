# Deploying TeamBoard

TeamBoard ships as **one container**: the API, serving the built Angular app, talking to a SQL Server database. Anything that can run a container, give it a SQL Server-compatible database and pass it environment variables can host it.

> **Status.** The image, the compose file and the CI job that builds the image are written, but **none of it has been run**: there is no Docker on the machine this was built on. The same steps were verified without Docker: the Release build, the `--migrate` step, the API serving the built app from a folder, the health endpoints, and the end-to-end tests against that setup. **No host is chosen and nothing is deployed**, so there is no live URL yet; the spec wanted the host picked together with GameVault's, on the day, once free-tier limits have been checked.

## What the host must provide

| Need | Why |
|---|---|
| Run a Linux container (port 8080) | the image |
| A SQL Server-compatible database (SQL Server, Azure SQL, a SQL Server container) | EF Core with the SQL Server provider |
| **WebSockets** | live updates; without them SignalR falls back to long polling, which works but is slower |
| HTTPS in front, with `X-Forwarded-Proto` and `X-Forwarded-For` | the app redirects to HTTPS, sends HSTS, and rate-limits by client address |
| **One instance** (for now) | live-update groups are in memory; see [ARCHITECTURE.md](ARCHITECTURE.md#scaling) |

## Configuration

Set as environment variables; a double underscore is a colon in the config path (`Jwt__Key` is `Jwt:Key`). **The app refuses to start** if a required one is missing or invalid, and says which.

| Variable | Required | Meaning |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | yes | the SQL Server connection string |
| `Jwt__Key` | yes | signs access tokens; at least 32 random characters (`openssl rand -base64 48`). Changing it signs everyone out |
| `ForwardedHeaders__Enabled` | behind a proxy | `true` makes the app trust `X-Forwarded-For/Proto` from the platform's proxy. **Leave it off if the container is reachable directly**: anyone could then choose the address the rate limiter sees |
| `Https__Redirect` | | default on outside development. Set `false` only for plain-http use such as `docker compose` on localhost |
| `Https__Port` | | the public HTTPS port for the redirect, default `443` |
| `Jwt__Issuer`, `Jwt__Audience` | | defaults `TeamBoard` and `TeamBoard.Web` |
| `RateLimit__AuthPermitPerMinute` | | per client address, per auth endpoint; default `10` |
| `Serilog__MinimumLevel__Default` | | default `Information`; logs are one JSON object per line on stdout |
| `Spa__Root` | | where the built Angular app is; default `wwwroot` (the image puts it there) |

Never put real values in the repository. `.env` is ignored by git; `.env.example` lists what it needs.

## Run it locally with Docker

```powershell
Copy-Item .env.example .env     # fill in SQL_PASSWORD and JWT_KEY
docker compose up --build       # http://localhost:8080
```

`docker compose` starts SQL Server, runs the migrations as a one-off container, and starts the app only after that succeeded.

## Release steps

1. **Build** the image: `docker build -t teamboard:1.0.0 .` (CI does this on every push).
2. **Migrate** the database as its own step, before the new version starts:
   ```
   docker run --rm -e ConnectionStrings__DefaultConnection="..." teamboard:1.0.0 --migrate
   ```
   It applies pending EF Core migrations, logs `Database migrated.` and exits 0. The app itself never migrates on startup in production. Migrations are written to be safe to run on a database with data in it (the first one moves the old tasks into personal boards; see [DEBUGGING.md](DEBUGGING.md#1-the-migration-that-would-have-eaten-the-data-m1)).
3. **Start** the container with the configuration above.
4. **Check** it:
   - `GET /health/live`: the process is up (use for restarts)
   - `GET /health/ready`: and it can reach the database (use for routing traffic)
   - then open the site, register, create a board.

## Logs and incidents

Logs are JSON on stdout. A user's report of "something went wrong" comes with a `correlationId`; searching the logs for it returns every line of that request, including the exception for a 500. A 409 on a card logs both row versions.

## Before going live: the checklist

- [ ] a host chosen, with its free-tier limits checked that day (database size, sleep-on-idle, WebSocket support)
- [ ] `ForwardedHeaders__Enabled=true`, and the app **not** reachable except through the platform's proxy
- [ ] a strong `Jwt__Key` that is not the one used anywhere else
- [ ] a backup plan for the database
- [ ] `loadtest/load.mjs` run against the real URL (`BASE=https://...`), to put a deployed number next to the local one in [PERFORMANCE.md](PERFORMANCE.md)
- [ ] the README's live link and the two-browser GIF filled in
