# Deploying TeamBoard

TeamBoard ships as **one container**: the API, serving the built Angular app, talking to a SQL Server database. Anything that can run a container, give it a SQL Server-compatible database and pass it environment variables can host it.

> **Status.** The image, the compose file and the CI job that builds the image are written, but **none of it has been run**: there is no Docker on the machine this was built on. The same steps were verified without Docker: the Release build, the `--migrate` step, the API serving the built app from a folder, the health endpoints, and the end-to-end tests against that setup. **The host is chosen (below) but nothing is deployed**, so there is no live URL yet. `deploy/azure.ps1` has never been run either: it needs an Azure account, and it is written from the CLI documentation.

## The chosen host: Azure Container Apps and Azure SQL

Decision record: [015](decisions/015-hosting.md). Chosen on 2026-11-20 from the current free-tier pages; **check them again before you deploy**, because limits change:

- [Azure SQL Database free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer): per database each month, 100,000 vCore-seconds, 32 GB of data and 32 GB of backup, renewing for the life of the subscription; auto-pauses when used up (the default) or bills the overage.
- [Azure Container Apps pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/): the first 180,000 vCPU-seconds, 360,000 GiB-seconds and 2 million requests each month are free; beyond that, per second.

**What you do, in order**

1. Create an Azure account (a card is required even for the free offers) and install the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli); then `az login` and `az extension add -n containerapp`.
2. Push to `main` and let CI publish `ghcr.io/<owner>/teamboard:latest` (the `image` job). On GitHub, open the package once and make it **public**, so Azure can pull it without credentials.
3. Run the script, one step at a time the first time:
   ```powershell
   .\deploy\azure.ps1 -Name teamboard-yourname -Image ghcr.io/<owner>/teamboard:latest
   ```
   It stops at the first failing step. New subscriptions are often refused in busy regions ("not accepting new customers"; West Europe did exactly that), so unless you pass `-Location` it tries several regions and uses the first that accepts a SQL server. It creates a resource group, the SQL server and the free database, a Container Apps environment, a one-off job that applies the migrations (the app is only created if that succeeds), and the app. It prints the address. `az group delete --name <name>-rg` removes all of it.
4. Open `https://<address>/health/ready`, register, create a board; then work through the checklist at the bottom of this page.

**What to expect**

- **Cold start.** The app scales to zero when idle, which keeps it inside the free grant. The first visit after a quiet spell waits for the container to start *and* for the serverless database to wake, about a minute. Pass `-AlwaysOn` (one replica always running) for a day you are showing it to someone; it uses more of the grant.
- **One replica only** (`--max-replicas 1`), because live-update groups are in memory.
- **Secrets** (the SQL connection string and the JWT key, which the script generates) are stored as Container Apps secrets and handed to the app as environment variables. Nothing is written to disk or to the repository.
- **When the free database allowance runs out** it auto-pauses until the next month, and the site shows errors until then. That is the price of the free offer.

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

- [x] a host chosen (Azure Container Apps and Azure SQL); its free-tier limits re-checked on the day you deploy
- [ ] `ForwardedHeaders__Enabled=true`, and the app **not** reachable except through the platform's proxy
- [ ] a strong `Jwt__Key` that is not the one used anywhere else
- [ ] a backup plan for the database
- [ ] `loadtest/load.mjs` run against the real URL (`BASE=https://...`), to put a deployed number next to the local one in [PERFORMANCE.md](PERFORMANCE.md)
- [ ] the README's live link and the two-browser GIF filled in
