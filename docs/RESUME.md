# Where each skill shows up

For someone reading this repository to judge it. Each claim points at something you can open and check.

| Skill | Where it is visible |
|---|---|
| **OOP and SOLID** | Small classes with one job and constructor-injected dependencies: `TokenService` and `AuthService` (`Auth/`), `GapPositionCalculator` (`Infrastructure/PositionCalculator.cs`), `BoardAccessHandler` (`Infrastructure/BoardAccess.cs`), `BoardConnections` and the broadcaster (`Realtime/`). Anything external sits behind an interface a test can replace: `ITokenService`, `IPositionCalculator`, `IBoardBroadcaster`, `IBoardEventHandler`, and on the front end `TeamBoardApi` and `HubLink`. |
| **Design patterns** | Observer (board events → activity log and live push), Strategy (position calculation), resource-based authorization, the middleware pipeline, validated Options. Table with file locations: [ARCHITECTURE.md](ARCHITECTURE.md#backend-feature-slices-dbcontext-used-directly). |
| **Modular architecture** | Feature folders on the API; `core / features / shared` on the front end with lazy routes. Diagram and layout: [ARCHITECTURE.md](ARCHITECTURE.md). |
| **API integrations** | A TypeScript client *generated* from the API's OpenAPI document, kept honest by a contract test; a SignalR client wrapped so tests can fake it. |
| **ASP.NET Core** | JWT with rotating refresh tokens and reuse detection, SignalR groups with per-join authorization, rate limiting, `ProblemDetails` with correlation ids, health checks, response compression, security headers and a CSP, EF Core with optimistic concurrency and a filtered unique index, startup option validation. |
| **Angular** | Standalone components, a signals store, functional interceptor and guards, CDK drag and drop with optimistic updates and rollback, lazy routes, a generated client, 128 unit tests, Playwright with two browser contexts. |
| **Debugging** | [DEBUGGING.md](DEBUGGING.md): eight real bugs with symptom, diagnosis and a test that keeps each fixed, including a 404 that only appeared when the app was served alongside the API. |
| **Performance** | [PERFORMANCE.md](PERFORMANCE.md): a 5,085 ms board load reduced to 10 ms, a fixed statement count enforced by tests, an index-seek query plan, a 50-user load test. |
| **Testing** | A permission matrix that runs *every* endpoint as every role and fails if an endpoint is missing from it; migrations tested against real old data; hub tests with real SignalR clients; mutation checks (a test is only trusted once it was seen to fail without the code it guards). |

## Resume bullets

Numbers are from this repository; the board-load figures are measured locally. A smaller run against the deployed app is in [PERFORMANCE.md](PERFORMANCE.md); from a machine about 230 ms away it did not meet the 200 ms target, mostly because of distance.

- Built **TeamBoard**, a real-time kanban app: ASP.NET Core 10 Web API, EF Core and SQL Server, SignalR, and an Angular 21 front end using a generated OpenAPI client.
- Implemented JWT access tokens with rotating, hashed refresh tokens (reuse detection) and board-level role authorization, covered by a permission-matrix integration test that exercises every endpoint as every role.
- Handled concurrent edits with optimistic concurrency (`RowVersion`, 409 with a recoverable UI) and optimistic UI updates; cut a 500-card board load from **5.1 s to 10 ms** (p50) by replacing an entity-graph query with flat projections, held to a fixed 7 SQL statements by a regression test; **p95 36 ms** at 50 concurrent users.
- Delivered live updates over SignalR with per-join authorization, echo suppression and resync on reconnect (a move reaches 50 clients in 44 ms p50 as a 122-byte message); verified end to end with a Playwright test driving two browsers against the built app under a strict Content-Security-Policy.

## The README plan, as built

Pitch, screenshots of two browsers in sync, how to run, tests and CI, performance numbers, architecture with diagrams, decision records, and this page. Live link: https://teamboard-taha.thankfulgrass-8dcd7388.centralus.azurecontainerapps.io. Still to add: a GIF of two browsers syncing.
