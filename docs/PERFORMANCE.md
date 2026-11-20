# Performance

Every claim here is a measurement from this repository, and each one can be reproduced with the command beside it. The raw output of the measurement tests is in [`perf-raw.txt`](perf-raw.txt).

**How to read the numbers.** They were taken on a development laptop with SQL Server Express on the same machine, so there is no network between the app and its database. The in-process tests also skip the network between browser and API. Treat them as a measure of *how much work the code does*, not as a promise about a deployed system; the shape of the result (a fixed handful of statements, milliseconds rather than seconds) is what carries over. A deployed number is still to be taken (see [Not done](#not-done)).

The target from the spec: a board of **500 cards loads in under 200 ms at the 95th percentile**.

| Area | Technique | Proof |
|---|---|---|
| Board load | Seven flat, tracking-free queries projected straight to DTOs | 5,085 ms → **10 ms** p50 for 500 cards; [details](#board-load) |
| Move path | One row updated, using a gap `Position`; renumber only when gaps run out | [6 statements, 1 write](#move) |
| Activity feed | Cursor paging on the `(BoardId, CreatedAt)` index | [Index seek, same cost on page 1 and page 102](#activity-feed) |
| Realtime | Messages carry ids and changed fields; one group per board | [122 bytes instead of 243 KB; 50 clients in 44 ms](#realtime) |
| HTTP | Brotli and gzip; hashed files cached for a year; lazy routes | [389 KB → 145 KB](#http) |
| Load | 50 simulated users on one 500-card board | [board load p95 **36 ms**, 0 errors](#load-test) |

## Board load

`GET /api/boards/{id}` returns the board with its columns, cards, members and labels in one response. Test board: 500 cards in 5 columns, 8 labels, 300 comments, 50 members. Run it with:

```powershell
$env:PERF = "1"; dotnet test TaskManager.Tests --filter "FullyQualifiedName~BoardLoad_500Cards"
```

Three versions of the same endpoint, all returning byte-identical JSON:

| | SQL statements | Latency (sequential) | Response |
|---|---|---|---|
| **First draft:** `Include` the whole entity graph, tracked, in one query | 2 | p50 **5,085 ms**, p95 5,484 ms (12 runs) | 242,904 B, 21,874 B gzip |
| M3: flat projections, no tracking | 8 | not measured | |
| **Now:** same, with the role read from the member list | **7** | p50 **10.2 ms**, p95 **19.0 ms**, p99 26.3 ms (200 runs) | 242,904 B, 21,890 B gzip |

**Why the first draft is slow.** Five collection `Include`s in one query make SQL Server return a Cartesian product: every member (50) times every label (8) times every card/label/comment combination (about 800), roughly 300,000 rows for a board with 500 cards. The database reports 45 ms because that is the time to the *first* row; the rest is the time to ship and materialise the rows, then EF's change tracker identifying every entity. Splitting the query (`AsSplitQuery`) would fix the explosion but would still load every card, label link and comment as a tracked entity, for data that is only going to be serialised and thrown away.

**Why the flat version is fast.** Each query touches one table (or one join), returns exactly the columns the DTO needs and is covered by an index; the pieces are assembled in memory with dictionaries. The comment count is a correlated `COUNT(*)` in the card query rather than a join, so comments never multiply rows.

**It stays seven statements however big the board is.** `QueryBudgetTests` asserts it: a 2-card board and a 500-card board both run 7 statements, so an accidental N+1 fails the build instead of showing up as slowness later.

## Move

Dragging a card sends `PATCH /api/tasks/{id}/move`. A card's `Position` is a `double`; a card dropped between two others gets the midpoint, so **one row is written**. A column is renumbered only when two neighbours are less than 1e-6 apart (`GapPositionCalculator`, with the rule covered by unit tests).

```
1  SELECT card                         (the card, to check its RowVersion early)
2  SELECT role                         (board permission)
3  SELECT EXISTS column on this board
4  SELECT sibling ids and positions    (to compute the position)
5  UPDATE Tasks ... WHERE Id AND RowVersion      <- the only write to the card
6  INSERT Activity
```

6 statements, 14.5 ms in the database for one move; over 150 sequential moves: p50 **6.6 ms**, p95 **9.1 ms**. `QueryBudgetTests` pins the count at six and asserts there is exactly one `UPDATE [Tasks]`.

## Activity feed

Paged by cursor, newest first, `(CreatedAt, Id)` as the cursor, so a page never counts and never skips rows. With 5,000 entries on a board:

```
  |--Top(TOP EXPRESSION:((31)))
       |--Nested Loops(Inner Join, OUTER REFERENCES:([a].[ActorId]))
            |--Nested Loops(Inner Join ... WITH ORDERED PREFETCH)
            |    |--Index Seek(OBJECT:[Activity].[IX_Activity_BoardId_CreatedAt] AS [a],
            |         SEEK:([a].[BoardId]=(1)) ORDERED BACKWARD)
            |    |--Clustered Index Seek(OBJECT:[Activity].[PK_Activity] ... LOOKUP)
            |--Clustered Index Seek(OBJECT:[Users].[PK_Users] ...)
```

An **ordered backward index seek**: SQL Server reads the index from the newest entry and stops after 31 rows. There is no sort and no scan. Measured: first page p50 2.7 ms / p95 4.2 ms; the page at cursor 102 (3,000 entries deep) p50 2.7 ms / p95 4.6 ms. Offset paging would get slower with every page. (The plan was captured with `SET SHOWPLAN_TEXT ON` on a dev database holding 5,000 generated entries.)

## Realtime

The hub only pushes, and only what changed. A `TaskMoved` message is **122 bytes**; the board it changes is 242,910 bytes, 1,991 times larger. One group per board means a change is sent to the people looking at that board and nobody else.

Fan-out with 50 clients connected to one board, from the move request being sent until the last client has the message, 25 moves:

| p50 | p95 | p99 |
|---|---|---|
| 43.9 ms | 62.3 ms | 196 ms |

This was measured with the *long-polling* transport over the in-memory test server (the test server has no real sockets), so it is a pessimistic figure: every client is waiting on an HTTP long poll rather than an open WebSocket. Run it with `PERF=1 dotnet test --filter LiveMove_FanOut`.

## HTTP

| | Raw | gzip | Brotli |
|---|---|---|---|
| Largest chunk (Angular core + Material) | 388,760 B | 157,835 B | **144,794 B** (37%) |
| Second | 130,096 B | 50,090 B | 46,997 B |
| Third | 112,233 B | 33,636 B | 31,359 B |
| Fourth | 84,063 B | 31,342 B | 28,987 B |

Measured with `curl` against the API serving the built app. Initial download for a first visit: **556 kB raw, 137 kB transferred** (Angular's estimate). Everything else is a lazy route, so signing in does not download the board:

| Chunk | Raw | Transferred |
|---|---|---|
| login / register | 3.5 / 4.8 kB | 1.4 / 1.6 kB |
| boards list | 15.7 kB | 3.9 kB |
| board (columns, drag and drop, activity, hub client) | 130.1 kB | 30.4 kB |
| card drawer | 17.6 kB | 5.0 kB |

Caching, verified by `SpaServingTests`: hashed bundles (`main-QX7OGRM2.js`) get `Cache-Control: public, max-age=31536000, immutable`; `index.html` gets `no-cache`, so a release shows up on the next load and old bundles are never fetched again. API responses are `no-store`.

## Load test

`loadtest/load.mjs`: 50 simulated users on one 500-card board against a real Kestrel server (Release build, Production configuration, SQL Server on the same machine). Each user repeats: 70% load the board, 20% move one of their own cards, 10% open the activity feed, then waits 100 to 300 ms.

```powershell
dotnet build TaskManager.API -c Release
# start the API with RateLimit__AuthPermitPerMinute=100000 (setup registers 50 users from one address), then:
node loadtest/load.mjs
```

30 seconds, 50 users, **7,002 requests (231 per second), 0 errors**:

| Endpoint | Requests | p50 | p95 | p99 | max |
|---|---|---|---|---|---|
| Board load | 4,864 | 11.0 ms | **35.9 ms** | 86.8 ms | 212 ms |
| Card move | 1,408 | 6.1 ms | 24.0 ms | 81.8 ms | 207 ms |
| Activity page | 730 | 5.3 ms | 12.8 ms | 31.7 ms | 187 ms |

The script exits non-zero if any request fails or board load p95 reaches 200 ms, so it can gate a pipeline.

**A deviation from the spec:** the plan called for a k6 script. k6 is not installed here, and a script I could not run is not evidence, so the load test is a Node script (Node 22's built-in `fetch`, no tool to install). It is about a hundred lines and ports to k6 mechanically.

## Not done

- **A measurement on the deployed API.** The spec's target is "under 200 ms at p95 on the deployed API". There is no deployment yet, so the figure above is local: 35.9 ms. Network distance and a cloud database add to it; the headroom (about 5x) is what makes the target likely to hold, but it is not yet shown. Run `loadtest/load.mjs` with `BASE=https://...` against the real URL once it exists.
- **The "before" for compression and lazy loading.** The tables show what the app ships now. A before/after for the lazy routes would need a build with them switched off, which I did not make.
- **Anything beyond one app server.** SignalR groups live in memory, so the live updates assume one instance. Scaling out needs a backplane (Redis or Azure SignalR); see [ARCHITECTURE.md](ARCHITECTURE.md#scaling).
