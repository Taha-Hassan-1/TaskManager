# 1. Repository

**Status:** Accepted

**Decision.** Keep the repository and its history (it started life as TaskManager); the product, and now the projects and the repository, are called TeamBoard.

**Why.** The existing commits and the GitHub URL stay valid, and finishing a started project reads better than starting another.

**Rejected.** A new repository.

**What happened.** Kept, and renamed at the end of the project: the solution, the two projects and their namespaces are `TeamBoard.*`, and the GitHub repository was renamed to match. The database names inside the connection strings were never tied to the old name, so no data moved.
