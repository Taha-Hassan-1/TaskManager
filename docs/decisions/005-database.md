# 5. Database

**Status:** Accepted

**Decision.** Keep SQL Server and align the EF Core packages with .NET 10.

**Why.** The existing migration and connection work, and the version mismatch (EF Core 9 on .NET 10) was a latent bug.

**Rejected.** Switching provider.

**What happened.** Done in M0: EF Core 10.0.15. EF Core 11 was available but needs .NET 11, so the 10.0 line is pinned. Tests run against a real SQL Server (SQL Express locally, a service container in CI), because the filtered unique index, `rowversion` and cascade rules are exactly what a fake would hide.
