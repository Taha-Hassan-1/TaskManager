# 2. API style

**Status:** Accepted

**Decision.** ASP.NET Core Web API with controllers, on .NET 10.

**Why.** It was already set up, and controllers are what most .NET job postings use.

**Rejected.** Minimal APIs only.

**What happened.** Held. One consequence worth knowing: a global filter that changes routing metadata (a `[Consumes]` filter) broke every GET once a second endpoint competed for the path; see DEBUGGING.md, bug 6.
