# 15. Hosting

**Status:** Accepted (host chosen, not yet deployed)

**Decision.** A Docker image, run on Azure Container Apps (one replica, scale to zero) with the database on Azure SQL Database's free offer.

**Why.** The app uses EF Core's SQL Server provider, and Azure SQL is the one managed SQL Server with a free tier that renews monthly (100,000 vCore-seconds, 32 GB). Container Apps has a monthly free grant (180,000 vCPU-seconds, 360,000 GiB-seconds, 2 million requests), runs one container behind a TLS-terminating proxy, which is what the forwarded-headers setting and the single-instance limit assume. Chosen on 2026-11-20 after checking the current free-tier pages; limits change, so check them again on the day of deploying.

**Rejected.** Fly.io (no free tier for new users), Render, Railway and Koyeb (no managed SQL Server: each would mean porting to Postgres or running SQL Server on a free VM), and a self-managed VM (more to look after).

**What happened.** Deployed on 2026-11-22 to an Azure free-trial subscription (Central US, the first region that accepted a new SQL server: three others refused). `deploy/azure.ps1` failed several times on the way and was fixed each time; the CI job publishes the image to ghcr.io and is green; `docker-compose.yml` was never run, because there is no Docker on the build machine. Costs to know: an Azure account needs a card even for free offers; scale-to-zero means the first visit after idle waits for a cold start and for the database to wake (about a minute); beyond the free grant Azure bills per second. **The trial ends on 20 December 2026**, after which everything pauses unless the subscription is upgraded; see [DEPLOY.md](../DEPLOY.md#the-free-trial-ends-on-20-december-2026).
