# Azure caption burst through a localhost bridge

This runbook documents the operator procedure proven on the maintained Windows catalogue on 2026-10-01 for temporarily accelerating Photo Identity generated captions with an Azure-hosted NVIDIA T4 while leaving Photo Identity itself unchanged and loopback-only.

The architecture exception is defined by [ADR-0011](../decisions/ADR-0011-operator-authorized-caption-burst-bridge.md). Normal day-to-day operation remains the local path in [Local operator guide](local-operator-guide.md) and [Local caption and narration evaluation](local-caption-narration-evaluation.md).

## Proven result

The accepted burst used the production caption worker already present on `main`:

- generation policy: `wi-0128-photo-caption-v3`;
- model: `qwen2.5vl:3b`;
- accepted exact model digest: `fb90415cde1ef08aa669ae74b082d49b158729b6db1ab183c941417d507e71a1`;
- image mode: temporary `thumbnail-480x320`;
- requested context: `1024`;
- timeout: `600` seconds;
- Azure Container Apps serverless GPU using one NVIDIA T4 replica during the burst;
- persistent Ollama model files on Azure Files;
- localhost bridge on the Photo Identity PC at `http://127.0.0.1:11435/`;
- Azure upstream exposed as HTTPS and source-IP restricted.

A like-for-like 12-photo evaluator comparison produced:

| Measure | Local Ollama | Azure T4 through bridge |
|---|---:|---:|
| Captions generated | 12/12 | 12/12 |
| Generation failures | 0 | 0 |
| Guard pass / flagged | 10 / 2 | 10 / 2 |
| Average client time | 122.18 s | 3.92 s |
| Median client time | 122.15 s | 1.74 s |
| Azure p95 | — | 27.89 s |
| Average speed-up | — | 31.15x |

The normal production worker was then enabled through the bridge and completed the outstanding caption backlog.

## Architecture and privacy boundary

~~~text
Maintained Windows Photo Identity PC                 Azure
------------------------------------                 -----
Photo Identity API + worker                          Container Apps HTTPS ingress
PostgreSQL catalogue                                 Ollama-compatible service
archive/review proxies                               qwen2.5vl:3b on NVIDIA T4
480x320 thumbnail rendering                          persistent model files
normalization + guard
caption persistence

Photo Identity -> http://127.0.0.1:11435
                    local reverse proxy
                         |
                         | HTTPS
                         v
              Azure Ollama /api/tags, /api/chat
~~~

The bridge forwards the current Ollama caption payload. The remote endpoint can see the 480x320 image content plus the caption prompt, model identifier and generation options. It does not receive PostgreSQL, file paths, identity assignments, face crops/embeddings, Places/GPS, capture dates or collection state from the caption request.

The thumbnail can still visibly contain private people, places, documents or other sensitive content. Treat the Azure endpoint as a system authorized to see each forwarded caption thumbnail.

The evaluator predates this bridge workflow and reports `external-photo-uploads=false` when its configured Ollama URL is loopback. That field is **not semantically correct for a bridged run** because the bridge forwards the thumbnail externally. Record bridged benchmark evidence as external inference even though the evaluator itself sees `127.0.0.1`.

## Two-computer operating model

The proven setup used two Windows computers only during provisioning:

- **Computer A — Photo Identity PC:** Photo Identity, PostgreSQL, review proxies, local Ollama, narration evaluator and the localhost bridge.
- **Computer B — Azure management PC:** Azure CLI and Azure resource administration.

Computer B is not part of the runtime inference path. After Azure ingress permits Computer A and the endpoint is healthy, Computer B can be shut down. Azure resources continue running independently.

## Durable non-secret state

Keep an operator-local PowerShell state file outside source control. It should contain names and identifiers needed to administer the retained resources, but no keys, passwords, tokens, connection strings or private paths.

Example:

~~~powershell
# azure-caption-state.ps1 -- operator-local, do not commit environment-specific values
$Rg         = "<resource-group>"
$Location   = "<azure-region>"
$Env        = "<container-apps-environment>"
$GpuProfile = "<gpu-workload-profile-name>"

$OllamaApp  = "<container-app-name>"
$OllamaFqdn = "<container-app-fqdn>"
$OllamaUrl  = "https://$OllamaFqdn"

$AcrName        = "<acr-name>"
$AcrLoginServer = "$AcrName.azurecr.io"
$OllamaImage    = "$AcrLoginServer/pi-ollama:investigation"

$StorageAccount = "<storage-account>"
$FileShare      = "ollama-models"
$StorageMount   = "ollamamodels"
$VolumeName     = "ollama-models-volume"
$ModelPath      = "/models"

$ModelName      = "qwen2.5vl:3b"
$ExpectedDigest = "fb90415cde1ef08aa669ae74b082d49b158729b6db1ab183c941417d507e71a1"
~~~

Load it in a future Azure-management shell with:

~~~powershell
. "<path>\azure-caption-state.ps1"
~~~

Never put ACR passwords, Azure Storage account keys, Azure access tokens, subscription credentials, PostgreSQL connection strings or public-IP history into the repository.

## Azure resource shape to retain

For easy reuse, retain these resources after a burst unless there is a reason to delete them:

- resource group;
- Container Apps managed environment;
- serverless GPU workload profile;
- Azure Container Registry and the custom non-root Ollama image;
- Azure Storage account + Azure Files share containing the Ollama model files;
- Container App configuration and ingress restrictions.

The GPU replica itself does not need to remain running. Set the Container App minimum replicas to zero after the burst.

### Custom non-root Ollama image

Azure Container Apps serverless GPU requires the application container to run non-root. The proven image used this Dockerfile:

~~~dockerfile
FROM ollama/ollama:latest

RUN groupadd --system --gid 10001 ollama-nonroot \
    && useradd --system \
       --uid 10001 \
       --gid 10001 \
       --no-create-home \
       --shell /usr/sbin/nologin \
       ollama-nonroot

ENV HOME=/tmp
ENV OLLAMA_MODELS=/tmp/ollama-models
ENV OLLAMA_HOST=0.0.0.0:11434

USER 10001:10001
~~~

Build through ACR:

~~~powershell
az acr build `
  --registry $AcrName `
  --image "pi-ollama:investigation" `
  .
~~~

For a future rebuild, prefer pinning a tested Ollama base image/version rather than relying indefinitely on `latest`. The exact caption-model digest remains the Photo Identity provenance boundary.

### Container App baseline

The accepted service shape was one GPU replica with 8 CPU and 56 GiB RAM on the GPU workload profile, external ingress to port 11434 and `maxReplicas=1`.

During an active backlog run:

~~~powershell
az containerapp update `
  --name $OllamaApp `
  --resource-group $Rg `
  --min-replicas 1 `
  --max-replicas 1
~~~

Keeping one replica warm avoids cold-start/model-load noise during the benchmark and bulk run.

After the run, return to:

~~~powershell
az containerapp update `
  --name $OllamaApp `
  --resource-group $Rg `
  --min-replicas 0 `
  --max-replicas 1
~~~

Current Azure Container Apps billing/scaling behavior should always be checked before a new burst. At the time of the accepted run, scale-to-zero was the intended idle state; serverless GPU replicas with `minReplicas=1` remained billable while running.

## Persistent Ollama model storage

Do not rely on the container-local `/tmp/ollama-models` store for a reusable service. The accepted setup mounted Azure Files at `/models` and set `OLLAMA_MODELS=/models`.

Create/attach an Azure Files share when building the environment for the first time:

~~~powershell
az storage account create `
  --resource-group $Rg `
  --name $StorageAccount `
  --location $Location `
  --kind StorageV2 `
  --sku Standard_LRS

az storage share-rm create `
  --resource-group $Rg `
  --storage-account $StorageAccount `
  --name $FileShare `
  --quota 20 `
  --enabled-protocols SMB

$StorageKey = az storage account keys list `
  --resource-group $Rg `
  --account-name $StorageAccount `
  --query "[0].value" `
  -o tsv

az containerapp env storage set `
  --name $Env `
  --resource-group $Rg `
  --storage-name $StorageMount `
  --storage-type AzureFile `
  --azure-file-account-name $StorageAccount `
  --azure-file-account-key $StorageKey `
  --azure-file-share-name $FileShare `
  --access-mode ReadWrite
~~~

Set the container environment variable:

~~~powershell
az containerapp update `
  --name $OllamaApp `
  --resource-group $Rg `
  --set-env-vars OLLAMA_MODELS=/models `
  --min-replicas 1 `
  --max-replicas 1
~~~

The accepted deployment mounted the environment storage using a container volume named `ollama-models-volume` at `/models` with SMB ownership/options suitable for UID/GID 10001. If rebuilding from scratch, validate the current Container Apps Azure Files schema before applying the mount; do not round-trip the full output of `az containerapp show` into an ARM PATCH because output-only properties such as `imageType` can be rejected by the update API.

After the mount is active, `/api/tags` is expected to be empty until the persistent share is populated.

Pull the model once into the persistent share:

~~~powershell
$PullBody = @{
    model  = $ModelName
    stream = $false
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri "$OllamaUrl/api/pull" `
  -ContentType "application/json" `
  -Body $PullBody `
  -TimeoutSec 1800
~~~

Verify that `/api/tags` still lists the model after restarting the active Container App revision. Persistence is proven only when the model survives a restart/new replica.

## Ingress restriction

The endpoint used for the burst should not be generally reachable from arbitrary source IPs. Add a source-IP allow rule for the Photo Identity PC or the NAT/public IP through which it reaches Azure.

A DNS-based way to inspect the current public IP when ordinary IP-check sites are blocked is:

~~~powershell
nslookup myip.opendns.com resolver1.opendns.com
~~~

From the Azure management PC:

~~~powershell
az containerapp ingress access-restriction set `
  --name $OllamaApp `
  --resource-group $Rg `
  --rule-name "PhotoIdentity-PC" `
  --ip-address "<PHOTO-IDENTITY-PUBLIC-IP>/32" `
  --action Allow `
  --description "Temporary Photo Identity caption inference"
~~~

If the public IP changes before a later burst, update the allow-list before troubleshooting the application or bridge.

## Verify Azure Ollama before involving Photo Identity

From Computer A:

~~~powershell
$AzureOllamaUrl = "https://<container-app-fqdn>"
$ModelName = "qwen2.5vl:3b"
$ExpectedDigest = "fb90415cde1ef08aa669ae74b082d49b158729b6db1ab183c941417d507e71a1"

Invoke-RestMethod "$AzureOllamaUrl/api/version"

$AzureTags = Invoke-RestMethod "$AzureOllamaUrl/api/tags"
$AzureModel = $AzureTags.models |
    Where-Object { $_.name -eq $ModelName -or $_.model -eq $ModelName } |
    Select-Object -First 1

$AzureDigest = ($AzureModel.digest -replace '^sha256:', '').ToLowerInvariant()
$AzureModel | Format-List

if ($AzureDigest -ne $ExpectedDigest) {
    throw "Unexpected Azure model digest: $AzureDigest"
}
~~~

Confirm GPU loading after one inference with:

~~~powershell
(Invoke-RestMethod "$AzureOllamaUrl/api/ps").models |
    Select-Object name,size,size_vram,context_length |
    Format-List
~~~

For the accepted T4 run, the loaded model was fully resident in VRAM (`size_vram == size`).

## Verify Local and Azure model identity

Before a burst intended to continue the same caption policy, compare the full digest returned by both endpoints:

~~~powershell
$LocalTags = Invoke-RestMethod "http://127.0.0.1:11434/api/tags"
$LocalModel = $LocalTags.models |
    Where-Object { $_.name -eq $ModelName -or $_.model -eq $ModelName } |
    Select-Object -First 1

$LocalDigest = ($LocalModel.digest -replace '^sha256:', '').ToLowerInvariant()

[pscustomobject]@{
    LocalDigest = $LocalDigest
    AzureDigest = $AzureDigest
    Match       = ($LocalDigest -eq $AzureDigest)
} | Format-List
~~~

Do not continue a same-policy backlog run unless `Match` is `True`. A tag name match is not enough.

## Start the localhost bridge on Computer A

The bridge exists because Photo Identity and the narration evaluator deliberately accept only loopback Ollama URLs. Do not weaken that application validation for this operational workflow.

Create an operator-local script such as `Start-AzureOllamaProxy.ps1` outside the repository:

~~~powershell
param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamBaseUrl
)

$ErrorActionPreference = "Stop"
$ListenUrl = "http://127.0.0.1:11435/"
$Upstream = [Uri]::new($UpstreamBaseUrl.TrimEnd("/") + "/")

$Listener = [System.Net.HttpListener]::new()
$Listener.IgnoreWriteExceptions = $true
$Listener.Prefixes.Add($ListenUrl)

$Handler = [System.Net.Http.HttpClientHandler]::new()
$Handler.AllowAutoRedirect = $false

$Client = [System.Net.Http.HttpClient]::new($Handler)
$Client.Timeout = [TimeSpan]::FromMinutes(15)

$Listener.Start()
Write-Host "Photo Identity Azure Ollama bridge" -ForegroundColor Cyan
Write-Host "Local:    $ListenUrl"
Write-Host "Upstream: $UpstreamBaseUrl"
Write-Host "Press Ctrl+C to stop."

while ($Listener.IsListening) {
    $Context = $Listener.GetContext()
    $Request = $null
    $Response = $null

    try {
        $Relative = $Context.Request.Url.PathAndQuery.TrimStart("/")
        $Target = [Uri]::new($Upstream, $Relative)
        $Method = [System.Net.Http.HttpMethod]::new($Context.Request.HttpMethod)
        $Request = [System.Net.Http.HttpRequestMessage]::new($Method, $Target)

        if ($Context.Request.HasEntityBody) {
            $Memory = [System.IO.MemoryStream]::new()
            try {
                $Context.Request.InputStream.CopyTo($Memory)
                $Body = $Memory.ToArray()
            }
            finally {
                $Memory.Dispose()
            }

            $Request.Content = [System.Net.Http.ByteArrayContent]::new($Body)
            if ($Context.Request.ContentType) {
                [void]$Request.Content.Headers.TryAddWithoutValidation(
                    "Content-Type",
                    $Context.Request.ContentType)
            }
        }

        if ($Context.Request.Headers["Accept"]) {
            [void]$Request.Headers.TryAddWithoutValidation(
                "Accept",
                $Context.Request.Headers["Accept"])
        }

        $Timer = [System.Diagnostics.Stopwatch]::StartNew()
        $Response = $Client.SendAsync(
            $Request,
            [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead
        ).GetAwaiter().GetResult()
        $Timer.Stop()

        $Context.Response.StatusCode = [int]$Response.StatusCode
        if ($Response.Content.Headers.ContentType) {
            $Context.Response.ContentType = $Response.Content.Headers.ContentType.ToString()
        }

        if ($Response.Content.Headers.ContentLength.HasValue) {
            $Context.Response.ContentLength64 = $Response.Content.Headers.ContentLength.Value
        }
        else {
            $Context.Response.SendChunked = $true
        }

        $ResponseStream = $Response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        try {
            $ResponseStream.CopyTo($Context.Response.OutputStream)
        }
        finally {
            $ResponseStream.Dispose()
        }

        Write-Host ("{0} {1} -> {2} ({3:N1}s)" -f
            $Context.Request.HttpMethod,
            $Context.Request.Url.PathAndQuery,
            [int]$Response.StatusCode,
            $Timer.Elapsed.TotalSeconds)
    }
    catch {
        Write-Host $_ -ForegroundColor Red
        $Bytes = [System.Text.Encoding]::UTF8.GetBytes($_.Exception.Message)
        try {
            $Context.Response.StatusCode = 502
            $Context.Response.ContentType = "text/plain; charset=utf-8"
            $Context.Response.ContentLength64 = $Bytes.Length
            $Context.Response.OutputStream.Write($Bytes, 0, $Bytes.Length)
        }
        catch {
        }
    }
    finally {
        if ($Response) { $Response.Dispose() }
        if ($Request) { $Request.Dispose() }
        try { $Context.Response.OutputStream.Close() } catch { }
    }
}
~~~

If `HttpListener` cannot bind, reserve the localhost URL once from an elevated PowerShell:

~~~powershell
$CurrentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
netsh http add urlacl url=http://127.0.0.1:11435/ user="$CurrentUser"
~~~

Start the bridge in its own normal PowerShell window:

~~~powershell
.\Start-AzureOllamaProxy.ps1 `
  -UpstreamBaseUrl $AzureOllamaUrl
~~~

Leave that window open for the entire burst.

Verify the bridge before running Photo Identity:

~~~powershell
Invoke-RestMethod "http://127.0.0.1:11435/api/version"
$ProxyTags = Invoke-RestMethod "http://127.0.0.1:11435/api/tags"
$ProxyTags.models | Select-Object name,digest,size | Format-Table -AutoSize
~~~

## Benchmark before enabling the production worker

Keep Automatic photo captions disabled while benchmarking.

The evaluator requires the same loopback contract as the production application, so point it at `127.0.0.1:11435` rather than the Azure HTTPS URL directly.

Set the PostgreSQL environment variable as in the normal WI-0128 runbook:

~~~powershell
$env:PHOTOIDENTITY_NARRATION_TEST = $env:PHOTOIDENTITY_POSTGRES_CONNECTION_STRING

if ([string]::IsNullOrWhiteSpace($env:PHOTOIDENTITY_NARRATION_TEST)) {
    $env:PHOTOIDENTITY_NARRATION_TEST =
        [Environment]::GetEnvironmentVariable(
            "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING",
            "User")
}
~~~

Set `$ProxyRoot` to the **same derivative root configured as `PhotoIdentity__ReviewProxyRoot`** for the running catalogue. Do not guess by appending `review-proxies` or the profile ID: stored relative proxy paths may already contain that segment.

~~~powershell
$ProxyRoot = "<configured PhotoIdentity__ReviewProxyRoot value>"
$ProxyProfile = "jpeg-1600-q78"
$CollectionId = "<representative saved Smart Collection GUID>"
~~~

Run a three-photo Azure smoke test first:

~~~powershell
$AzureSmokeReport = ".\artifacts\caption-azure-t4-smoke.json"

dotnet run --project src/PhotoIdentity.Cli -c Release -- narration evaluate `
  --postgres-connection-env PHOTOIDENTITY_NARRATION_TEST `
  --collection $CollectionId `
  --proxy-root $ProxyRoot `
  --proxy-profile $ProxyProfile `
  --ollama-base-url "http://127.0.0.1:11435" `
  --model $ModelName `
  --caption-image-mode thumbnail `
  --ollama-context 1024 `
  --target-count 50 `
  --sample-count 3 `
  --timeout-seconds 600 `
  --report $AzureSmokeReport
~~~

If the evaluator reports `proxy-unavailable`, that failure occurs before Ollama. Re-check the configured derivative root/profile and the selected collection before troubleshooting Azure.

For a like-for-like benchmark, use the same collection and sample count against local and bridged Azure:

~~~powershell
$LocalReport = ".\artifacts\caption-local-12.json"
$AzureReport = ".\artifacts\caption-azure-t4-12.json"

# Local
dotnet run --project src/PhotoIdentity.Cli -c Release -- narration evaluate `
  --postgres-connection-env PHOTOIDENTITY_NARRATION_TEST `
  --collection $CollectionId `
  --proxy-root $ProxyRoot `
  --proxy-profile $ProxyProfile `
  --ollama-base-url "http://127.0.0.1:11434" `
  --model $ModelName `
  --caption-image-mode thumbnail `
  --ollama-context 1024 `
  --target-count 50 `
  --sample-count 12 `
  --timeout-seconds 600 `
  --report $LocalReport

# Azure through localhost bridge
dotnet run --project src/PhotoIdentity.Cli -c Release -- narration evaluate `
  --postgres-connection-env PHOTOIDENTITY_NARRATION_TEST `
  --collection $CollectionId `
  --proxy-root $ProxyRoot `
  --proxy-profile $ProxyProfile `
  --ollama-base-url "http://127.0.0.1:11435" `
  --model $ModelName `
  --caption-image-mode thumbnail `
  --ollama-context 1024 `
  --target-count 50 `
  --sample-count 12 `
  --timeout-seconds 600 `
  --report $AzureReport
~~~

The report excludes caption text. Use it for timing/provenance/guard counts, then inspect actual production captions after a small live batch.

## Start a production backlog burst

Stop an already running Photo Identity instance before changing its process environment.

Keep the bridge running. In the PowerShell session that will launch the packaged application:

~~~powershell
$env:PhotoIdentity__CaptionEnrichment__OllamaBaseUrl = "http://127.0.0.1:11435/"
$env:PhotoIdentity__CaptionEnrichment__Model = "qwen2.5vl:3b"
$env:PhotoIdentity__CaptionEnrichment__ContextTokens = "1024"
$env:PhotoIdentity__CaptionEnrichment__TimeoutSeconds = "600"

& ".\.artifacts\packages\PhotoIdentity-win-x64\PhotoIdentity.cmd"
~~~

These are process-environment overrides for the temporary run. The ordinary local default remains `http://127.0.0.1:11434/` when the override is absent.

From another PowerShell, enable English captions through the existing API:

~~~powershell
$Api = "http://127.0.0.1:5080"

$Body = @{
    enabled  = $true
    language = "en"
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Put `
  -Uri "$Api/api/caption-enrichment/settings" `
  -ContentType "application/json" `
  -Body $Body |
  Format-List *
~~~

The settings response can still show the previous worker-state snapshot immediately after enabling. The worker polls durable settings; check again after several seconds:

~~~powershell
Start-Sleep -Seconds 8
Invoke-RestMethod "$Api/api/caption-enrichment/status" | Format-List *
~~~

Expected active evidence includes `enabled=True`, `language=en`, generation version `wi-0128-photo-caption-v3`, worker state `running` or later `idle`, and repeated `POST /api/chat -> 200` lines in the bridge console.

## Monitor and inspect captions

Primary worker status:

~~~powershell
Invoke-RestMethod "$Api/api/caption-enrichment/status" | Format-List *
~~~

The bridge console provides a second independent signal that production requests are reaching Azure:

~~~text
GET /api/tags -> 200 (...)
POST /api/chat -> 200 (...)
POST /api/chat -> 200 (...)
~~~

To inspect recently persisted evidence, run a read-only query through the normal PostgreSQL operator tooling:

~~~sql
SELECT
    asset_revision_id,
    language,
    generation_version,
    model_id,
    model_digest,
    content,
    risk_flags,
    generation_milliseconds,
    generated_at_utc
FROM photo_generated_captions
WHERE language = 'en'
  AND generation_version = 'wi-0128-photo-caption-v3'
ORDER BY generated_at_utc DESC
LIMIT 20;
~~~

Open a selected row in Photo Identity:

~~~powershell
$RevisionId = "<asset_revision_id>"
Start-Process "http://127.0.0.1:5080/photo/$RevisionId"
~~~

The Photo Details page displays guard-passing generated captions. Guard-blocked evidence remains stored but is not exposed as display text.

A direct API lookup is also available:

~~~powershell
Invoke-RestMethod "$Api/api/photos/$RevisionId/caption" | Format-List *
~~~

## Confirm completion

When the backlog is exhausted, the status should settle at `idle` with a message that no current photos are waiting for the configured language/policy.

Record a simple completion summary before shutting down the burst:

~~~sql
SELECT
    language,
    generation_version,
    model_digest,
    COUNT(*) AS caption_rows,
    COUNT(*) FILTER (WHERE cardinality(risk_flags) = 0) AS guard_pass,
    COUNT(*) FILTER (WHERE cardinality(risk_flags) > 0) AS guard_flagged,
    ROUND(AVG(generation_milliseconds)::numeric / 1000, 2) AS avg_seconds
FROM photo_generated_captions
WHERE generation_version = 'wi-0128-photo-caption-v3'
GROUP BY language, generation_version, model_digest
ORDER BY language, model_digest;
~~~

Do not interpret the row count as archive coverage without considering current revisions/language/policy. The worker's `idle` state plus the expected current-photo coverage is the operational completion signal.

## OFF: finish the burst safely

When the backlog is complete:

1. disable Automatic photo captions;
2. stop Photo Identity;
3. stop the localhost bridge;
4. remove the temporary caption endpoint environment override by using a fresh shell or clearing the variables;
5. restart Photo Identity normally against local Ollama;
6. scale the Azure Container App to zero.

Disable the worker:

~~~powershell
$Body = @{
    enabled  = $false
    language = "en"
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Put `
  -Uri "$Api/api/caption-enrichment/settings" `
  -ContentType "application/json" `
  -Body $Body |
  Format-List *
~~~

Stop Photo Identity and the bridge with `Ctrl+C` in their respective consoles.

Return to ordinary local configuration either by opening a new PowerShell or clearing the temporary variables:

~~~powershell
Remove-Item Env:PhotoIdentity__CaptionEnrichment__OllamaBaseUrl -ErrorAction SilentlyContinue
Remove-Item Env:PhotoIdentity__CaptionEnrichment__Model -ErrorAction SilentlyContinue
Remove-Item Env:PhotoIdentity__CaptionEnrichment__ContextTokens -ErrorAction SilentlyContinue
Remove-Item Env:PhotoIdentity__CaptionEnrichment__TimeoutSeconds -ErrorAction SilentlyContinue

& ".\.artifacts\packages\PhotoIdentity-win-x64\PhotoIdentity.cmd"
~~~

Normal Photo Identity then returns to its default local endpoint `http://127.0.0.1:11434/` unless an operator has configured another loopback URL.

On the Azure-management PC, scale the GPU service to zero:

~~~powershell
. "<path>\azure-caption-state.ps1"

az containerapp update `
  --name $OllamaApp `
  --resource-group $Rg `
  --min-replicas 0 `
  --max-replicas 1
~~~

Verify:

~~~powershell
az containerapp show `
  --name $OllamaApp `
  --resource-group $Rg `
  --query "properties.template.scale" `
  -o json
~~~

Keep Azure Files/ACR/environment resources if fast future reactivation is desirable. They can incur storage/registry costs even with no GPU replica, so review actual Azure cost data periodically.

## ON again: future backlog reactivation

For a later backlog, the shortest safe procedure is:

1. load the non-secret Azure state file on the management PC;
2. confirm/update the Photo Identity PC source-IP allow rule;
3. set `minReplicas=1`, `maxReplicas=1` for the active burst;
4. call `/api/version` and `/api/tags` from Computer A;
5. verify the Azure Files-backed model is still present; pull it again only if necessary;
6. compare the full local and Azure model digests;
7. start the localhost bridge and verify `/api/version` and `/api/tags` through `127.0.0.1:11435`;
8. run a three-photo evaluator smoke test and, if useful, a small like-for-like benchmark;
9. restart Photo Identity with the temporary `CaptionEnrichment__OllamaBaseUrl` override pointing to `127.0.0.1:11435`;
10. enable Automatic photo captions and inspect a small production sample;
11. let the backlog drain while monitoring status and bridge traffic;
12. perform the OFF procedure and return the GPU service to `minReplicas=0`.

Do not skip the digest comparison merely because the tag still says `qwen2.5vl:3b`.

## Troubleshooting

### `/api/version` works but `/api/tags` is empty

The Container App is healthy but Ollama's configured model directory is empty. Confirm `OLLAMA_MODELS=/models`, the Azure Files volume is mounted at `/models`, and the retained share is the expected one. If the share is intentionally empty, pull the model once.

### Model is present but the digest differs

Stop before enabling production enrichment if the intent is to continue the same policy. Either restore the exact matching model artifact or accept that the different digest is different provenance and may create additional candidate work after returning local.

### Evaluator says `proxy-unavailable=N`

This occurs before Azure inference. Confirm `$ProxyRoot` is the same configured derivative root used by Photo Identity and that the selected revisions have metadata for the requested proxy profile. The evaluator also rejects missing files, wrong encoded lengths and reparse-point paths.

### Direct Azure works but the localhost bridge returns 502

Check the upstream HTTPS URL, Container Apps ingress allow-list, DNS/TLS connectivity and bridge console error. Confirm no stale public-IP restriction is blocking Computer A.

### Photo Identity still uses local Ollama

Photo Identity reads the caption endpoint at process startup. Stop the existing application and launch it from the same PowerShell session in which `PhotoIdentity__CaptionEnrichment__OllamaBaseUrl=http://127.0.0.1:11435/` was set.

### Enabling captions still shows `state=disabled`

The API persists `enabled=True` immediately but the worker-state snapshot can remain on the previous disabled iteration until its next poll. Check again after roughly 5-10 seconds. If `lastActivityAtUtc` no longer advances, inspect the application process/logs.

### Worker repeatedly reports model/storage failure

Check `/api/tags` through the bridge, exact digest, review-proxy availability, upstream ingress and bridge process. The worker has normal retry/backoff; do not assume it silently falls back to local Ollama.

### Azure costs continue after the backlog

Confirm `minReplicas=0`. A bridge or Photo Identity process stopping does not itself scale down a Container App configured with a minimum of one replica.

## Related documentation

- [ADR-0011: operator-authorized remote caption bursts through a localhost bridge](../decisions/ADR-0011-operator-authorized-caption-burst-bridge.md)
- [ADR-0010: local production execution](../decisions/ADR-0010-local-production-execution.md)
- [Local caption and narration evaluation](local-caption-narration-evaluation.md)
- [Local operator guide](local-operator-guide.md)
- [Azure cost controls](../azure/cost-controls.md) — historical general Azure reference; validate current Container Apps/GPU billing before each burst
