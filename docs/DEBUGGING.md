# Debugging notes

Real bugs from building this project: what it looked like, how it was found, what fixed it, and the test that keeps it fixed. They are in the order they happened.

## How to find things in a running system

- **Every request has a correlation id.** It is the `X-Correlation-Id` response header, the `correlationId` field of every error body, and a property on every log line (the logs are one JSON object per line in production). Give a user's error to `grep` and you have the whole request.
- **A conflict is logged with both versions.** A 409 on a card logs the `RowVersion` the client sent and the one in the database, so "two people moved the same card" can be reproduced.
- **What SQL a request runs** is visible to tests through `SqlCapture` (`TaskManager.Tests/Perf/PerfKit.cs`), which is how the statement budgets in [PERFORMANCE.md](PERFORMANCE.md) are enforced.
- **Startup fails loudly.** A missing JWT key, issuer or database string stops the app at boot with the field named, rather than on the first login.

---

## 1. The migration that would have eaten the data (M1)

**Symptom.** `dotnet ef migrations add` printed *"An operation was scaffolded that may result in the loss of data."* Reading the generated file: `RenameColumn("UserId" → "Priority")` and `DropColumn("IsCompleted")`.

**Diagnosis.** EF guesses a rename by comparing column types: the old `UserId` (int) disappeared, a new `Priority` (int) appeared, so it assumed one became the other. Run as generated, every task would have lost its owner and its completed flag.

**Fix.** The migration was rewritten by hand: rename `UserId` to `CreatedById`; add `BoardId` and `ColumnId` as *nullable*; a data step creates a "My tasks" board per user, puts completed tasks in Done, the rest in To do, in `CreatedAt` order; only then drop `IsCompleted` and make the new columns required. The `Down` migration restores `IsCompleted` from the column a card sits in.

**Kept fixed by.** `DomainMigrationTests` builds the *old* schema, inserts old-style rows, migrates forward and asserts every task is in the right place; it also migrates back. Lesson: read every scaffolded migration, and test any migration that touches existing rows against real rows.

## 2. A valid token that still got a 401 (M2)

**Symptom.** `GET /api/me` with a freshly issued token returned 401, but the response had *no* `WWW-Authenticate` header.

**Diagnosis.** The missing header was the clue: when authentication fails the Bearer handler always adds it. So authentication had *succeeded* and the 401 came from my own controller: `FindFirst("sub")` was null. The JWT handler maps inbound claim names to long URIs by default, so `sub` had become `…/ClaimTypes/nameidentifier`.

**Fix.** `MapInboundClaims = false`, so claims keep the names they were issued with.

**Kept fixed by.** `AuthTests.Me_RequiresAValidToken`. Lesson: *where* a 401 comes from is information; check the headers before the code.

## 3. Three cards, one position (M4)

**Symptom.** Three cards added in quick succession came back in a different order than they were added, and one title had the previous ones glued onto it.

**Diagnosis.** The database showed all three cards with `Position = 1024`. Each request computed "last position + 1024" while the previous one was still in flight, so each saw an empty column. (The glued titles were the test driver typing faster than the page re-rendered; a separate, smaller bug.) Two people adding to one column at once would do the same.

**Fix.** Order by `(Position, Id)` everywhere (the board load, move siblings, column renumbering, the client's sort) so ties are stable. A move *between* two tied cards finds no gap, takes the existing renumbering path, and the tie disappears.

**Kept fixed by.** `CardsWithEqualPositions_AreOrderedByIdAndCanBeMovedBetween`.

## 4. A guard that would have crashed on the one case it exists for (M4)

**Symptom.** The unit test for the route guard that handles "board not found" threw `NG0203: inject() must be called from an injection context`.

**Diagnosis.** The guard called `inject(Notifier)` in its `catch` block, *after* an `await`. Angular's `inject()` only works synchronously; after the first `await` the injection context is gone. The happy path never reached it, so manual testing would not have either: only a 404 would, and it would have thrown instead of showing the message.

**Fix.** All three `inject()` calls moved to the top of the guard.

**Kept fixed by.** `guards.spec.ts`, which exercises 403, 404 and a 500.

## 5. The same moment, shown as two different times (M5)

**Symptom.** In the activity panel an entry pushed live showed *17:27* while entries loaded over REST, written at about the same time, showed *11:47*.

**Diagnosis.** The user is in UTC+5, and 17:27 was right. The pushed entry came straight from memory with `DateTimeKind.Utc`, which serialises with a trailing `Z`. Entries read back from SQL Server have `Kind = Unspecified` (the column has no zone), which serialises with **no** `Z`, and a browser reads a zone-less timestamp as local time. This affected every timestamp the API ever returned, not just this panel.

**Fix.** A value converter in `AppDbContext.ConfigureConventions` marks every `DateTime` as UTC when it is read.

**Kept fixed by.** `EveryTimestamp_IsSentAsUtc`, which was verified by removing the fix and watching it fail on the missing `Z`.

## 6. Every API call returned 404, but only when the app was being served (M6)

**Symptom.** In the first end-to-end run the boards page showed "Not Found". `curl` showed `/health/live` fine and `/boards` returning the app, but `/api/boards`, `/api/me` and `/api/auth/refresh` all 404.

**Diagnosis.** The backend test suite had passed, because it never ran the API *with* the Angular folder present. First a test was written that reproduced it (`RealApiRoutes_StillWork_WhileTheAppIsServed`), then pieces were removed one at a time:

| Experiment | Result |
|---|---|
| remove the SPA fallback endpoint | routes work again, so the fallback is involved |
| replace the regex fallback with a plain `MapFallback` handler | still 404, and now the 404 came from *my own handler*: the fallback was being **chosen over** the controller |
| remove the global `[Consumes("application/json")]` filter | all green |

That filter had been added in M4 to make the generated client send `application/json`. A GET has no `Content-Type`; when only one endpoint matches a path, ASP.NET's consumes policy is skipped, but when a second candidate exists (the fallback) the policy runs and drops the controller for a request with no content type. `IsOptional = true` did not change that.

**Fix.** Remove the filter. The OpenAPI document transformer added later already reduces every request and response to `application/json`, and the generated spec was verified **byte-for-byte unchanged**, so the Angular client is not affected.

**Kept fixed by.** `RealApiRoutes_StillWork_WhileTheAppIsServed`. Lesson: a global filter that changes routing metadata is a latent bug that only appears when something else competes for a path; and "the suite is green" says nothing about a configuration the suite does not run.

## 7. A hashed bundle that came back as `index.html` (M6)

**Symptom.** A test asserting that `main-ABCD1234.js` is cached for a year got `Cache-Control: no-cache`.

**Diagnosis.** A probe printed the actual response: status 200, `text/html`, body `<app-root>`. The *file* was being answered with the app shell. ASP.NET inserts routing at the very start of the pipeline unless asked not to, so the SPA fallback endpoint had already matched every path, and the static file middleware deliberately stands aside once an endpoint is selected. In production this would have served HTML for every script and stylesheet.

**Fix.** Call `UseRouting()` explicitly, *after* the static files.

**Kept fixed by.** `SpaServingTests.HashedBundles_AreCachedForGood` (and its neighbours).

## 8. An unstyled page that the tests did not notice (M6)

**Symptom.** In a failure screenshot from the end-to-end run, the page was missing all its padding and layout.

**Diagnosis.** The built `index.html` loads the main stylesheet with Angular's critical-CSS trick: `<link rel="stylesheet" media="print" onload="this.media='all'">`. The strict Content-Security-Policy (`script-src 'self'`) blocks inline event handlers, so `onload` never ran and the stylesheet stayed print-only. Nothing failed; the app just looked wrong.

**Fix.** `inlineCritical: false` in the production build, so the stylesheet is an ordinary link. The built `index.html` now has no inline script or handler.

**Kept fixed by.** The Playwright tests listen for console messages and fail on any CSP violation, so the next change that breaks the policy fails the build instead of looking subtly wrong.
