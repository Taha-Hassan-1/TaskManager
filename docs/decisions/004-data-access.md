# 4. Data access

**Status:** Accepted

**Decision.** EF Core with `DbContext` used directly in the feature code; no repository layer.

**Why.** `DbContext` already is a repository and a unit of work. GameVault shows the repository pattern.

**Rejected.** A repository per entity.

**What happened.** Held. Logic that deserves its own tests (positions, permissions, tokens) is pulled into small classes rather than hidden behind repositories.
