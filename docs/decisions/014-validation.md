# 14. Validation

**Status:** Accepted

**Decision.** Data annotations with automatic `ProblemDetails` responses.

**Why.** Enough for this model; no extra library.

**Rejected.** A validation library.

**What happened.** Held. The Angular forms mirror the same rules (username pattern, password 10 to 72 characters) so most mistakes are caught before a request, and server messages are shown on the matching field.
