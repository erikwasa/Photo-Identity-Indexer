# Azure burst captioning

This guide explains how to use an operator-controlled Azure GPU endpoint as temporary caption-inference capacity for a Photo Identity backlog.

This is an operational use of the narrow Remote caption-inference exception accepted in [ADR-0011](../decisions/ADR-0011-operator-authorized-remote-caption-inference.md). It is **not** a return to the historical Azure processing architecture. Photo Identity, PostgreSQL, archive access, candidate selection, caption policy and persistence remain on the maintainer-controlled Windows computer.

For the provider-neutral evaluator and caption-policy details, see [Local caption and narration evaluation](local-caption-narration-evaluation.md).

## What "Azure burst captioning" means

Azure supplies only an Ollama-compatible HTTPS inference endpoint with suitable GPU capacity. Photo Identity temporarily points its existing caption worker at that endpoint, lets the normal backlog drain, and then returns to local Ollama.

~~~text
Windows Photo Identity host                         Azure GPU endpoint
---------------------------                         ------------------
PostgreSQL catalogue                               Ollama-compatible HTTPS
archive/review proxies                             selected vision model
candidate selection
480x320 thumbnail render
prompt/policy + guard
caption persistence

          480x320 JPEG + prompt + model/options  ---------->
          <---------- generated caption response
~~~

The worker remains serial. "Burst" means using faster temporary inference hardware; it does not fan one backlog out across multiple workers or move catalogue processing into Azure.

Transport location is not part of caption policy identity. If the Azure endpoint exposes the same model name and exact digest and the other policy fields are unchanged, captions produced remotely satisfy the same candidate identity as local captions. Returning to Local therefore does not regenerate that backlog only because the transport changed.

There is no automatic fallback. If Remote inference fails, Photo Identity reports the failure and follows the normal retry/backoff behavior rather than silently switching to Local.

## Privacy boundary

Before using Remote mode, treat the Azure endpoint as a system that can see the image content sent for captioning.

For each caption request Photo Identity sends only:

- a locally rendered 480x320 JPEG;
- the neutral caption prompt;
- the configured model identifier; and
- Ollama generation/protocol options.

The thumbnail is decoded and re-encoded locally so source EXIF is not forwarded. Photo Identity does **not** send original-file bytes, source or review-proxy paths, person assignments, face crops, embeddings, Places/GPS, capture dates, collection membership, PostgreSQL connection information or unrelated catalogue metadata.

People, places, documents or other sensitive content visibly present in the thumbnail are naturally still visible to the Azure endpoint. Use an endpoint and retention policy you trust.

## Azure endpoint prerequisites

This repository does not provision Azure infrastructure. Start this procedure after an operator-controlled Azure workload is available.

The endpoint must:

1. expose the Ollama-compatible `/api/tags` and `/api/chat` APIs;
2. have the selected vision model installed before Photo Identity connects;
3. use a final absolute **HTTPS** URL with a trusted TLS certificate;
4. be reachable from the maintained Windows host without following an HTTP redirect; and
5. have independent access restrictions appropriate to the hosting platform, such as source-network/IP allow-listing where practical.

Photo Identity's current Remote contract does not add a bearer/API-key header. An Azure front end that requires a per-request application authorization header is therefore not directly supported. Do not put credentials, tokens or signed query parameters in the endpoint URL: URLs containing user information, a query string or a fragment are rejected.

The initial production model is normally `qwen2.5vl:3b`. The endpoint may use another compatible model, but a different exact digest is intentionally treated as different model provenance.

## 1. Define the Azure endpoint

In the PowerShell session on the maintained Windows computer:

~~~powershell
$azureCaptionEndpoint = "https://<your-azure-ollama-host>/"
$modelName = "qwen2.5vl:3b"
~~~

Use the final externally reachable Ollama base URL. A routing base path is supported, but the URL must not contain credentials, a query string or a fragment.

Check that the endpoint responds and that the model is present:

~~~powershell
$remoteTags = Invoke-RestMethod `
    ($azureCaptionEndpoint.TrimEnd('/') + "/api/tags")

$remoteModel = $remoteTags.models |
    Where-Object { $_.name -eq $modelName }

$remoteModel | Select-Object name, digest, size
~~~

If no row is returned, install/pull the model on the Azure endpoint before continuing.

## 2. Verify the exact model digest

When the goal is to accelerate the existing Local backlog without changing caption model identity, compare Azure with the local Ollama inventory:

~~~powershell
$localModel = (Invoke-RestMethod `
    "http://127.0.0.1:11434/api/tags").models |
    Where-Object { $_.name -eq $modelName }

$localModel  | Select-Object name, digest, size
$remoteModel | Select-Object name, digest, size
~~~

Compare the full normalized SHA-256 digest, not only the tag name.

- Matching digest: Remote and Local can satisfy the same model part of the caption policy when the remaining policy fields also match.
- Different digest: Remote captions retain that different provenance and normal candidate-policy behavior applies after returning Local.

Do not relabel a different digest as equivalent merely to avoid regeneration.

## 3. Benchmark Azure before enabling the backlog

Leave **Settings -> Automatic photo captions** disabled while benchmarking. The narration evaluator is read-only and can deliberately use Remote mode.

Set the normal evaluator inputs:

~~~powershell
$env:PHOTOIDENTITY_NARRATION_TEST = `
    $env:PHOTOIDENTITY_POSTGRES_CONNECTION_STRING

if ([string]::IsNullOrWhiteSpace($env:PHOTOIDENTITY_NARRATION_TEST)) {
    $env:PHOTOIDENTITY_NARRATION_TEST =
        [Environment]::GetEnvironmentVariable(
            "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING",
            "User")
}

$proxyRoot = Join-Path $env:LOCALAPPDATA "PhotoIdentity\review-proxies"
$collectionId = "<saved Smart Collection GUID>"
~~~

Run a small Azure sample first:

~~~powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- narration evaluate `
  --postgres-connection-env PHOTOIDENTITY_NARRATION_TEST `
  --collection $collectionId `
  --proxy-root $proxyRoot `
  --proxy-profile "jpeg-1600-q78" `
  --inference-mode Remote `
  --ollama-base-url $azureCaptionEndpoint `
  --model $modelName `
  --caption-image-mode thumbnail `
  --ollama-context 1024 `
  --sample-count 4 `
  --timeout-seconds 600 `
  --report artifacts/caption-azure.json `
  --review-output "$env:TEMP\PhotoIdentity\azure-caption-review"
~~~

Remote mode sends bounded thumbnails only and rejects explicit full-proxy mode. The report records inference mode, endpoint host, exact model digest, image mode, context, timing and guard counts without storing captions or URL secrets.

Review the private sample on the Windows computer:

~~~powershell
Start-Process "$env:TEMP\PhotoIdentity\azure-caption-review\index.html"
~~~

Check both speed and output quality before turning the archive worker loose on a large backlog. For a like-for-like performance comparison, run the same evaluator command against Local with the same collection, sample size, model digest, thumbnail mode and context.

## 4. Start the temporary Azure backlog run

Stop Photo Identity before changing deployment configuration.

In the PowerShell session that will launch Photo Identity:

~~~powershell
$env:PhotoIdentity__CaptionEnrichment__InferenceMode = "Remote"
$env:PhotoIdentity__CaptionEnrichment__OllamaBaseUrl = $azureCaptionEndpoint
$env:PhotoIdentity__CaptionEnrichment__Model = $modelName
$env:PhotoIdentity__CaptionEnrichment__ContextTokens = "1024"
$env:PhotoIdentity__CaptionEnrichment__TimeoutSeconds = "600"

& ".\.artifacts\packages\PhotoIdentity-win-x64\PhotoIdentity.cmd"
~~~

If deployment JSON already owns these settings, change them there instead of maintaining two conflicting configuration sources. Remove any conflicting legacy `PhotoIdentity:GeneratedCaptions:OllamaBaseUrl` setting.

Endpoint selection is operator/deployment configuration and is intentionally not editable from an unauthenticated browser client.

In **Settings -> Automatic photo captions**:

1. confirm inference mode is **Remote**;
2. confirm the displayed endpoint host is the expected Azure host;
3. confirm the configured model and resolved digest;
4. select the desired generation language;
5. enable automatic captions; and
6. watch the worker state/message and Remote-processing notice.

For the first live test, let only a small batch complete, disable automatic captions, and inspect the stored captions/provenance/guard results. Re-enable it only after that check is satisfactory.

The background worker continues independently of pages, Smart Collections and slideshows. Opening a photo does not prioritize it for captioning.

## 5. Monitor the run

Use the Automatic photo captions settings/status as the primary operator view. Confirm periodically that:

- mode still says Remote;
- the endpoint host is the expected Azure host;
- the resolved model digest has not unexpectedly changed;
- the worker is generating rather than repeatedly failing/backing off; and
- the remaining caption backlog is decreasing.

Remote failures never trigger an automatic Local fallback. If the endpoint is unhealthy, disable enrichment or stop the application, correct the endpoint, and then resume deliberately.

Azure resource utilization, quota and billing are separate from Photo Identity. Use Azure's own monitoring/cost controls while the temporary GPU capacity exists, and stop/delete the temporary capacity when the backlog run is finished if that matches the chosen Azure deployment model.

## 6. Return to normal Local captioning

Disable **Automatic photo captions** and stop Photo Identity.

Restore Local mode in the same configuration source used to launch the application:

~~~powershell
$env:PhotoIdentity__CaptionEnrichment__InferenceMode = "Local"
$env:PhotoIdentity__CaptionEnrichment__OllamaBaseUrl = `
    "http://127.0.0.1:11434/"

& ".\.artifacts\packages\PhotoIdentity-win-x64\PhotoIdentity.cmd"
~~~

In Settings/status, confirm:

- mode is **Local**;
- the endpoint is loopback;
- the expected local model is selected; and
- the resolved digest matches the intended model provenance.

Re-enable automatic captions if normal gradual local processing should continue.

No database migration or caption rewrite is required. With the same model digest, language, prompt/generation version, thumbnail mode and context, captions created during the Azure burst remain valid and are not regenerated merely because the endpoint moved back to localhost.

After Photo Identity is safely back on Local mode, remove or stop the temporary Azure endpoint using the hosting platform's own process.

## Troubleshooting

### Photo Identity rejects the Azure URL at startup

Remote mode requires an absolute HTTPS URL. Remove credentials, query strings and fragments and use the final non-redirecting endpoint. Plain HTTP is intentionally rejected.

### `/api/tags` works in PowerShell but Photo Identity cannot generate captions

Verify `/api/chat` is exposed through the same base path and does not require a bearer/API-key header that Photo Identity does not send. Also check Azure-side network allow-listing and TLS/certificate validity.

### The model tag matches but the digest differs

Treat it as a different model revision. The exact digest is the retained provenance boundary. Either install the matching model revision on Azure or accept that Remote and Local will represent different caption-policy candidates.

### Remote processing stops after failures

There is no fallback by design. The worker keeps the normal observable retry/backoff behavior. Fix or disable the Remote endpoint rather than expecting Photo Identity to silently use Local Ollama.

### Returning Local appears to create more work

Check the model digest and all other policy inputs: language, prompt/generation version, thumbnail image mode and requested context. Transport alone does not create a new policy, but a changed model or policy does.

## Related documentation

- [ADR-0011: operator-authorized remote caption inference](../decisions/ADR-0011-operator-authorized-remote-caption-inference.md)
- [Local caption and narration evaluation](local-caption-narration-evaluation.md)
- [ADR-0010: local production execution](../decisions/ADR-0010-local-production-execution.md)
- [Azure cost controls](../azure/cost-controls.md) — historical general Azure cost-control reference; validate service-specific controls against the Azure workload actually used
