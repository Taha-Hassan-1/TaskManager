# 6. Authentication

**Status:** Accepted

**Decision.** A JWT access token (15 minutes) plus a rotating refresh token (7 days) kept hashed in the database; BCrypt for passwords.

**Why.** It shows token authentication done by hand. GameVault shows ASP.NET Core Identity.

**Rejected.** ASP.NET Core Identity here.

**What happened.** Done in M2, with reuse detection (a rotated token turning up again revokes the user's tokens) and an atomic claim so two simultaneous refreshes cannot both succeed.
