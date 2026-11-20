# 3. Project structure

**Status:** Accepted

**Decision.** One API project organised by feature (Auth, Boards, Tasks, Comments, Activity) with a shared Infrastructure folder; tests in a second project.

**Why.** It is deliberately different from GameVault's layered projects, so the two repositories show both approaches, and it needed the least refactoring of the existing code.

**Rejected.** Four-project layering again.

**What happened.** Held. The test project references the API project directly and drives it through `WebApplicationFactory`, so nothing needs to be public just to be testable.
