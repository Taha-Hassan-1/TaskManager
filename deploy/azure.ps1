<#
.SYNOPSIS
  Deploys TeamBoard to Azure: Azure SQL Database (free offer) + Azure Container Apps (one container).

.DESCRIPTION
  Written from the Azure CLI documentation and fixed as it met a real subscription; expect the occasional rough edge.
  It stops at the first failing step, and re-running it is safe after `az group delete --name <name>-rg`.
  See docs/DEPLOY.md.

  Needs: the Azure CLI (az), signed in with `az login`, with the containerapp extension (`az extension add -n containerapp`),
  and the image already pushed to a registry the platform can pull anonymously (CI pushes ghcr.io/<owner>/teamboard on main;
  make that package public once in GitHub, Packages).

  What it creates (all in one resource group, so `az group delete` removes everything):
    - an Azure SQL logical server and ONE database using the free offer (auto-pauses when the monthly allowance is used)
    - a Container Apps environment, a one-off job that applies the migrations, and the app itself
    - the app scales to zero when idle (free, slow first visit) unless you pass -AlwaysOn

  Region: new subscriptions are often refused in busy regions ("not accepting new customers"). Unless you pass -Location,
  the script tries several regions and uses the first one that accepts a SQL server. All resources then live in that region.

.EXAMPLE
  .\deploy\azure.ps1 -Name teamboard-taha -Image ghcr.io/taha-hassan-1/teamboard:latest
#>
param(
  [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9-]{2,30}$')][string]$Name,  # becomes part of the web address, lowercase
  [Parameter(Mandatory)][string]$Image,
  [string]$Location = '',   # leave empty to try several regions
  [switch]$AlwaysOn         # keep one replica running: no cold start, uses more of the free grant
)

$ErrorActionPreference = 'Stop'
function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

# PowerShell does not stop when a native command fails, so every az call goes through here and throws on a non-zero exit.
# (Only the first words of the command are shown in the error: the full line can contain a password.)
function Invoke-Az {
  & az @args
  if ($LASTEXITCODE -ne 0) { throw "The Azure CLI failed (exit $LASTEXITCODE): az $(($args | Select-Object -First 3) -join ' ') ..." }
}

$group = "$Name-rg"; $sqlServer = "$Name-sql"; $database = 'teamboard'; $envName = "$Name-env"; $app = $Name; $job = "$Name-migrate"

# Secrets are made or asked for here and never written to a file or echoed.
$sqlAdmin = 'teamboardadmin'
$sqlPassword = Read-Host 'Choose a password for the SQL admin account (12+ chars, upper, lower, digit, symbol)' -AsSecureString
$sqlPasswordPlain = [System.Net.NetworkCredential]::new('', $sqlPassword).Password

if ($sqlPasswordPlain.Length -lt 12) { throw 'The SQL password must be at least 12 characters.' }
# The Azure CLI on Windows is a .cmd wrapper, which misreads these characters in an argument.
if ($sqlPasswordPlain -match '[&|<>^%"''`]') { throw 'Please choose a SQL password without these characters: & | < > ^ % " '' `' }

# Windows PowerShell 5.1 (the one that ships with Windows) runs on the older .NET Framework, which has no static
# RandomNumberGenerator.GetBytes; this form works there and in PowerShell 7.
$jwtBytes = New-Object byte[] 48
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($jwtBytes) } finally { $rng.Dispose() }
$jwtKey = [Convert]::ToBase64String($jwtBytes)

# ---- the resource group, then a region for the SQL server ----
$candidates = if ($Location) { @($Location) } else { @('northeurope', 'uksouth', 'eastus2', 'centralus', 'swedencentral', 'westus2', 'francecentral') }

Step 'Resource group'
# A resource group is only a label: the things in it may live in other regions. So it is created once (and reused if a
# failed run left one behind), and only the SQL server's region is varied below. Deleting and recreating the group
# between attempts raced with Azure ("resource group not found" straight after creating it).
$exists = (Invoke-Az group exists --name $group --output tsv | Out-String).Trim()
if ($exists -ne 'true') { Invoke-Az group create --name $group --location $candidates[0] --output none }

# The SQL server comes first because it is where a refused region shows up fastest.
Step 'SQL server (finding a region that accepts new customers)'
$refusal = 'RequestDisallowedByAzure|locationineligible|not accepting new customers|LocationNotAvailableForResourceType|RegionDoesNotAllowProvisioning'
$chosen = $null
foreach ($candidate in $candidates) {
  Write-Host "Trying region '$candidate'..."
  $output = ''; $exit = 1
  for ($attempt = 1; $attempt -le 4; $attempt++) {
    # Captured, not shown: a refused region is expected and handled below; anything else is a real error.
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $output = & az sql server create --name $sqlServer --resource-group $group --location $candidate `
      --admin-user $sqlAdmin --admin-password $sqlPasswordPlain --output none 2>&1 | Out-String
    $exit = $LASTEXITCODE
    $ErrorActionPreference = $previous

    if ($exit -eq 0 -or $output -match $refusal) { break }
    # A group created a moment ago can briefly be "not found" by the SQL service: wait and ask again.
    if ($output -match 'ResourceGroupNotFound|ResourceNotFound' -and $attempt -lt 4) { Start-Sleep -Seconds 10; continue }
    break
  }

  if ($exit -eq 0) { $chosen = $candidate; break }

  # An earlier run, even one that reported an error, can have created the server. Keep it and its region, and set its
  # admin password to the one typed now (this run's connection string uses it; the old one may have been different).
  $existing = [regex]::Match($output, "already exists in location '([a-z0-9]+)'")
  if ($existing.Success) {
    $chosen = $existing.Groups[1].Value
    Write-Host "  The SQL server already exists in '$chosen' from an earlier run; reusing it." -ForegroundColor Yellow
    Invoke-Az sql server update --name $sqlServer --resource-group $group --admin-password $sqlPasswordPlain --output none
    break
  }

  if ($output -match $refusal) {
    Write-Host "  '$candidate' is not accepting new SQL servers for this subscription." -ForegroundColor Yellow
    continue
  }
  throw "Creating the SQL server failed for a reason other than the region:`n$output"
}
if (-not $chosen) { throw "None of these regions accepted a SQL server: $($candidates -join ', '). Try another with -Location (for example one near you), or check the subscription in the portal." }
$Location = $chosen
Write-Host "Using region '$Location'." -ForegroundColor Green

# "Allow Azure services": the app and the migration job connect from inside Azure.
Invoke-Az sql server firewall-rule create --server $sqlServer --resource-group $group --name AllowAzureServices `
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 --output none

Step 'The free database'
Invoke-Az sql db create --server $sqlServer --resource-group $group --name $database `
  --edition GeneralPurpose --family Gen5 --capacity 2 --compute-model Serverless `
  --use-free-limit --free-limit-exhaustion-behavior AutoPause --output none

# A long timeout and retries: the first connection after an auto-pause has to wake the database (about a minute).
$connection = "Server=tcp:$sqlServer.database.windows.net,1433;Database=$database;User ID=$sqlAdmin;Password=$sqlPasswordPlain;" +
              "Encrypt=True;TrustServerCertificate=False;Connect Timeout=90;ConnectRetryCount=5;ConnectRetryInterval=10"

Step 'Container Apps environment'
Invoke-Az containerapp env create --name $envName --resource-group $group --location $Location --output none

Step 'Apply the migrations (a one-off job running the same image)'
Invoke-Az containerapp job create --name $job --resource-group $group --environment $envName `
  --trigger-type Manual --replica-timeout 900 --replica-retry-limit 0 `
  --image $Image --cpu 0.5 --memory 1.0Gi '--args=--migrate' `
  --secrets "conn=$connection" --env-vars 'ConnectionStrings__DefaultConnection=secretref:conn' --output none
$execution = (Invoke-Az containerapp job start --name $job --resource-group $group --query name --output tsv | Out-String).Trim()
if (-not $execution) { throw 'The migration job did not start (no execution name came back).' }
Write-Host "Migration job started: $execution. Waiting..."
do {
  Start-Sleep -Seconds 10
  $status = (Invoke-Az containerapp job execution show --name $job --resource-group $group --job-execution-name $execution --query properties.status --output tsv | Out-String).Trim()
  Write-Host "  $status"
} while ($status -in 'Running', 'Processing', 'Unknown')
if ($status -ne 'Succeeded') {
  throw "The migration job ended as '$status', so the app was NOT deployed. Its logs are in the portal: resource group $group, job $job, execution $execution."
}

Step 'The app'
$minReplicas = if ($AlwaysOn) { 1 } else { 0 }
Invoke-Az containerapp create --name $app --resource-group $group --environment $envName --image $Image `
  --ingress external --target-port 8080 --min-replicas $minReplicas --max-replicas 1 `
  --cpu 0.5 --memory 1.0Gi `
  --secrets "conn=$connection" "jwt=$jwtKey" `
  --env-vars 'ConnectionStrings__DefaultConnection=secretref:conn' 'Jwt__Key=secretref:jwt' 'ForwardedHeaders__Enabled=true' --output none

$fqdn = (Invoke-Az containerapp show --name $app --resource-group $group --query properties.configuration.ingress.fqdn --output tsv | Out-String).Trim()
Write-Host "`nDeployed: https://$fqdn" -ForegroundColor Green
Write-Host "Check: https://$fqdn/health/ready   (the first request after idle can take a minute while the database wakes)"
Write-Host "Remove everything later with: az group delete --name $group"
