# 13. Serving

**Status:** Accepted

**Decision.** In production the API serves the built Angular files, so there is no CORS; in development `ng serve` proxies `/api` and `/hubs` to the API.

**Why.** One deployable, and a whole class of CORS bugs removed.

**Rejected.** Separate hosts.

**What happened.** Done in M6 with cache rules (hashed bundles for a year, `index.html` never) and a fallback so client-side routes survive a refresh. It exposed two ordering bugs; see DEBUGGING.md, bugs 6 to 8.
