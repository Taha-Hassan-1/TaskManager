# 15. Hosting

**Status:** Accepted (host chosen, not yet deployed)

**Decision.** A Docker image, run on Azure Container Apps (one replica, scale to zero) with the database on Azure SQL Database's free offer.

**Why.** The app uses EF Core's SQL Server provider, and Azure SQL is the one managed SQL Server with a free tier that renews monthly (100,000 vCore-seconds, 32 GB). Container Apps has a monthly free grant (180,000 vCPU-seconds, 360,000 GiB-seconds, 2 million requests), runs one container behind a TLS-terminating proxy, which is what the forwarded-headers setting and the single-instance limit assume. Chosen on 2026-11-20 after checking the current free-tier pages; limits change, so check them again on the day of deploying.

**Rejected.** Fly.io (no free tier for new users), Render, Railway and Koyeb (no managed SQL Server: each would mean porting to Postgres or running SQL Server on a free VM), and a self-managed VM (more to look after).

**What happened.** The image, `docker-compose.yml`, the CI job that publishes the image to ghcr.io on main, `deploy/azure.ps1` and docs/DEPLOY.md are written but **never run**: there was no Azure account or Docker on the build machine. Costs to know: an Azure account needs a card even for free offers; scale-to-zero means the first visit after idle waits for a cold start and for the database to wake (about a minute); beyond the free grant Azure bills per second. **Nothing is deployed yet.**
