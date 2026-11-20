# 15. Hosting

**Status:** Open

**Decision.** A Docker image; the host is chosen at M6 together with GameVault's.

**Why.** Free tiers change, so the choice is made on the day.

**Rejected.** Deciding now.

**What happened.** The image, `docker-compose.yml`, the CI job that builds the image and docs/DEPLOY.md are written but **not yet run** (no Docker on the build machine). **No host is chosen and nothing is deployed.**
