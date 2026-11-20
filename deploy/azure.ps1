<#
.SYNOPSIS
  Deploys TeamBoard to Azure: Azure SQL Database (free offer) + Azure Container Apps (one container).

.DESCRIPTION
  UNTESTED. Written from the Azure CLI documentation, never run: no Azure account was available when it was written.
  Read it, run the steps one at a time the first time, and fix what the CLI tells you. See docs/DEPLOY.md.

  Needs: the Azure CLI (az), signed in with `az login`, with the containerapp extension (`az extension add -n containerapp`),
  and the image already pushed to a registry the platform can pull anonymously (CI pushes ghcr.io/<owner>/teamboard on main;
  make that package public once in GitHub, Packages).

  What it creates (all in one resource group, so `az group delete` removes everything):
    - an Azure SQL logical server and ONE database using the free offer (auto-pauses when the monthly allowance is used)
    - a Container Apps environment, a one-off job that applies the migrations, and the app itself
    - the app scales to zero when idle (free, slow first visit) unless you pass -AlwaysOn

.EXAMPLE
  .\deploy\azure.ps1 -Name teamboard-taha -Image ghcr.io/taha-hassan-1/teamboard:latest
#>
param(
  [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9-]{2,30}$')][string]$Name,  # becomes part of the web address, lowercase
  [Parameter(Mandatory)][string]$Image,
  [string]$Location = 'westeurope',
  [switch]$AlwaysOn   # keep one replica running: no cold start, uses more of the free grant
)

$ErrorActionPreference = 'Stop'
function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

$group = "$Name-rg"; $sqlServer = "$Name-sql"; $database = 'teamboard'; $env = "$Name-env"; $app = $Name; $job = "$Name-migrate"

# Secrets are made or asked for here and never written to a file or echoed.
$sqlAdmin = 'teamboardadmin'
$sqlPassword = Read-Host 'Choose a password for the SQL admin account (12+ chars, upper, lower, digit, symbol)' -AsSecureString
$sqlPasswordPlain = [System.Net.NetworkCredential]::new('', $sqlPassword).Password
$jwtKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))

Step 'Resource group'
az group create --name $group --location $Location --output none

Step 'SQL server and the free database'
az sql server create --name $sqlServer --resource-group $group --location $Location `
  --admin-user $sqlAdmin --admin-password $sqlPasswordPlain --output none
# "Allow Azure services": the app and the migration job connect from inside Azure.
az sql server firewall-rule create --server $sqlServer --resource-group $group --name AllowAzureServices `
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 --output none
az sql db create --server $sqlServer --resource-group $group --name $database `
  --edition GeneralPurpose --family Gen5 --capacity 2 --compute-model Serverless `
  --use-free-limit --free-limit-exhaustion-behavior AutoPause --output none

# A long timeout and retries: the first connection after an auto-pause has to wake the database (about a minute).
$connection = "Server=tcp:$sqlServer.database.windows.net,1433;Database=$database;User ID=$sqlAdmin;Password=$sqlPasswordPlain;" +
              "Encrypt=True;TrustServerCertificate=False;Connect Timeout=90;ConnectRetryCount=5;ConnectRetryInterval=10"

Step 'Container Apps environment'
az containerapp env create --name $env --resource-group $group --location $Location --output none

Step 'Apply the migrations (a one-off job running the same image)'
az containerapp job create --name $job --resource-group $group --environment $env `
  --trigger-type Manual --replica-timeout 900 --replica-retry-limit 0 `
  --image $Image --cpu 0.5 --memory 1.0Gi --args '--migrate' `
  --secrets "conn=$connection" --env-vars 'ConnectionStrings__DefaultConnection=secretref:conn' --output none
$execution = az containerapp job start --name $job --resource-group $group --query name --output tsv
Write-Host "Migration job started: $execution. Waiting..."
do {
  Start-Sleep -Seconds 10
  $status = az containerapp job execution show --name $job --resource-group $group --job-execution-name $execution --query properties.status --output tsv
  Write-Host "  $status"
} while ($status -in 'Running', 'Processing')
if ($status -ne 'Succeeded') { throw "The migration job ended as '$status'. See its logs in the portal; the app was NOT deployed." }

Step 'The app'
$minReplicas = if ($AlwaysOn) { 1 } else { 0 }
az containerapp create --name $app --resource-group $group --environment $env --image $Image `
  --ingress external --target-port 8080 --min-replicas $minReplicas --max-replicas 1 `
  --cpu 0.5 --memory 1.0Gi `
  --secrets "conn=$connection" "jwt=$jwtKey" `
  --env-vars 'ConnectionStrings__DefaultConnection=secretref:conn' 'Jwt__Key=secretref:jwt' 'ForwardedHeaders__Enabled=true' --output none

$fqdn = az containerapp show --name $app --resource-group $group --query properties.configuration.ingress.fqdn --output tsv
Write-Host "`nDeployed: https://$fqdn" -ForegroundColor Green
Write-Host "Check: https://$fqdn/health/ready   (the first request after idle can take a minute while the database wakes)"
Write-Host "Remove everything later with: az group delete --name $group"
