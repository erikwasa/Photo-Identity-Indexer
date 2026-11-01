# Approved inference configuration in launcher.json

Photo Identity's maintained Windows installation has one operator-owned
`%LOCALAPPDATA%\PhotoIdentity\launcher.json` (or the explicitly selected
`PHOTOIDENTITY_LAUNCHER_CONFIG`). The launcher remains the authority for
the **API environment**. Operator-only metadata can live alongside it
without becoming application configuration or enabling remote inference.

This is an opt-in, backwards-compatible migration. Existing configurations
without these keys continue using the historical application defaults.
Do not copy hashes from an example blindly: **approve the specific files
and Ollama installation first**. No personal media or secrets belong in
the configuration file or a GitHub PR.

## Maintained Windows configuration: observed acceptance (2026-11-02)

The maintainer reports that the **current private** `launcher.json` is configured
for a **separately started, operator-controlled Azure Ollama bridge listening
on `http://127.0.0.1:11434/`**. Port 11434 is the *bridge*, **not a local
Ollama model installation**; the bridge forwards to the operator-selected
`operator.azureOllamaUpstreamUrl`. The prior Azure 403/RBAC problem was
resolved by the operator. The upstream address, bridge authorization and
service lifecycle remain operator responsibilities; the launcher does not
start or scale Azure.

| Check | Maintainer observation | Evidence scope |
| --- | --- | --- |
| `Start-PhotoIdentity.ps1 -ConfigurationPath $launcherPath -ValidateConfigurationOnly` | **Passed**, `catalogueProvider: postgresql` | Configuration parsing/validation; does not start the app |
| `Get-PhotoIdentityApprovedInference.ps1 -ConfigurationPath $launcherPath` | **Completed without error** | Actual on-disk CLIP model, vocabulary and merges matched launcher SHA-256 pins |
| `GET http://127.0.0.1:11434/api/version` and `/api/tags` | **Responded**; model `qwen2.5vl:3b`, digest matched approval | The operator identified the port's listener as the Azure bridge, not locally installed Ollama |
| `Get-PhotoIdentityApprovedInference.ps1 -ConfigurationPath $launcherPath -VerifyOllama` | **`OllamaVerified = True`** | Live loopback model inventory and approved digest check; not itself an inference request |
| Photo Identity caption generation and persistence | **Passed, maintainer-reported in a separate chat** | One end-to-end photo-caption write with the same launcher settings; no private photo, SQL row or transcript is checked into Git |

Approved identities observed for this deployment:

- CLIP ONNX SHA-256: `42b5dac8f0a73e45b1626ae7a69b41a4bc111c6fe1edafcceda8c2a5506dc6db`
- CLIP vocabulary SHA-256: `5047b556ce86ccaf6aa22b3ffccfc52d391ea4accdab9c2f2407da5b742d4363`
- CLIP merges SHA-256: `9fd691f7c8039210e0fced15865466c65820d09b63988b0174bfe25de299051a`
- Ollama `qwen2.5vl:3b` SHA-256: `fb90415cde1ef08aa669ae74b082d49b158729b6db1ab183c941417d507e71a1`; context `1024`, timeout `600` seconds.

**Result:** Configuration preflight, live approved-model identity and a
production caption-persistence smoke test are accepted on the maintained
Windows host. No further configuration change is needed while this bridge
continues to listen on port 11434 and the approved model remains unchanged.
This is operator-reported deployment evidence, **not** a claim of CI
execution, ongoing Azure availability, full backlog quality coverage,
restart/recovery acceptance or any WI-0190 video-frame acceptance.
Automatic caption enablement/language remains governed by PostgreSQL
application settings.

For a repeat check from the repository root:

```powershell
$launcherPath = Join-Path $env:LOCALAPPDATA "PhotoIdentity\launcher.json"
.\Start-PhotoIdentity.ps1 -ConfigurationPath $launcherPath -ValidateConfigurationOnly
$verified = .\Get-PhotoIdentityApprovedInference.ps1 -ConfigurationPath $launcherPath -VerifyOllama
$verified.OllamaVerified   # Expected: True while the approved bridge is running
```

The configured loopback origin proves only the API-to-bridge hop; the
bridge forwards caption thumbnails to the operator-controlled Azure
infrastructure. **Do not interpret a loopback URL as local-only processing.**
Do not copy the maintainer's remote URL, private paths or personal media
into repository examples.

## Feasibility review

| Candidate | Decision | Consumer and boundary |
| --- | --- | --- |
| Caption Ollama base URL | **Supported** | `settings.PhotoIdentity__CaptionEnrichment__OllamaBaseUrl` feeds API; loopback HTTP(S) **only**. The selected video CLI and registered pilot now accept an **explicit** `--ollama-base-url` for the same loopback origin. |
| Caption Ollama model name | **Supported** | `settings.PhotoIdentity__CaptionEnrichment__Model` feeds API; `Get-PhotoIdentityApprovedInference.ps1` supplies CLI name. |
| Approved Ollama model SHA-256 | **Supported, opt-in pin** | `settings.PhotoIdentity__CaptionEnrichment__ApprovedModelSha256`. API refuses caption inference if `/api/tags` reports a different installed digest; the read-only helper checks the configured digest against Ollama when `-VerifyOllama` is requested. The video CLI still enforces `--ollama-sha256`. |
| Caption context tokens and timeout | **Supported for photo captions** | `settings.PhotoIdentity__CaptionEnrichment__ContextTokens` (256–32768) and `TimeoutSeconds` (30–1800). Video-frame inference has separate reviewed time budgets and a different prompt; **do not** silently synchronize those bounds. |
| CLIP model directory | **Supported** | `settings.PhotoIdentity__SemanticSearch__ModelDirectory` was already read by photo search but previously blocked by launcher; the helper derives the ONNX/vocab/merges paths for CLI. |
| Approved CLIP model, vocab and merges hashes | **Supported, opt-in pins** | `settings.PhotoIdentity__SemanticSearch__ModelSha256`, `VocabularySha256`, `MergesSha256`. The API checks configured pins before initializing CLIP; the helper checks **all three** before returning CLI arguments. Existing video evidence provenance continues to use the exact ONNX model hash. |
| Azure bridge upstream origin | **Operator-only** | Top-level `operator.azureOllamaUpstreamUrl`: credential-free remote HTTPS origin. The launcher validates but **does not export it to the API**. Only a separately started, controlled bridge can contact it. |
| FFmpeg/FFprobe paths and hashes | **Already supported** | Keep using `Get-PhotoIdentityApprovedVideoTools.ps1`; all four values already reside in `settings`. |
| Caption enabled/language | **Do not duplicate** | Persistent PostgreSQL settings are controlled from the app UI; launcher values would create conflicting authorities. |
| Photo/video prompt version, thumbnail/decoder geometry, sampling, safety deadlines | **Do not centralize** | These are versioned processing contracts and evidence/provenance boundaries, not deployment knobs. |
| PostgreSQL passwords, Azure tokens/keys, storage credentials | **Never in launcher** | Keep secret material in approved environment/identity stores; launcher retains only the PostgreSQL *environment-variable name*. |
| Azure resource group, app name, replica scale, ingress ACL, public IP | **Not API/CLI runtime** | Maintain in a separate non-secret Azure management state on its own machine. No implicit startup or scale change. |

## Example schema (replace the three hashes with reviewed values)

This is a **fragment**, not a full `launcher.json` and not a ready-to-use
credential. Add entries to the existing `settings` object rather than
replacing it. The CLIP ONNX hash below comes from the already-pinned
WI-0126 model installer; the tokenizer hashes must be confirmed against
the approved downloaded revision. The demonstrated Ollama digest is from
the earlier reviewed WI-0128 Azure experiment and may **not** match every
installed local model.

```json
{
  "settings": {
    "PhotoIdentity__SemanticSearch__ModelDirectory": "%LOCALAPPDATA%\\PhotoIdentity\\Models\\WI-0126\\clip-vit-base-patch32-b318363",
    "PhotoIdentity__SemanticSearch__ModelSha256": "42b5dac8f0a73e45b1626ae7a69b41a4bc111c6fe1edafcceda8c2a5506dc6db",
    "PhotoIdentity__SemanticSearch__VocabularySha256": "<reviewed 64-character lowercase SHA-256>",
    "PhotoIdentity__SemanticSearch__MergesSha256": "<reviewed 64-character lowercase SHA-256>",
    "PhotoIdentity__CaptionEnrichment__OllamaBaseUrl": "http://127.0.0.1:11434/",
    "PhotoIdentity__CaptionEnrichment__Model": "qwen2.5vl:3b",
    "PhotoIdentity__CaptionEnrichment__ApprovedModelSha256": "fb90415cde1ef08aa669ae74b082d49b158729b6db1ab183c941417d507e71a1",
    "PhotoIdentity__CaptionEnrichment__ContextTokens": "1024",
    "PhotoIdentity__CaptionEnrichment__TimeoutSeconds": "600"
  },
  "operator": {
    "azureOllamaUpstreamUrl": "https://<your-controlled-azure-container-app-origin>"
  }
}
```

The JSON above is **illustrative only**. Angle-bracket placeholders fail
digest/URL validation until replaced. Keep the rest of your existing
launcher keys, especially the PostgreSQL reference, video tool pins and
archive settings. To route captions through an authorized bridge, use
its actual loopback listening origin (the maintained host currently uses
port **11434**; the historical October bridge procedure used **11435**) in
`CaptionEnrichment__OllamaBaseUrl`; **never** put an Azure hostname there.

## PowerShell and CLI usage

From the repository root, after approving and entering hashes:

```powershell
$inference = .\Get-PhotoIdentityApprovedInference.ps1
# Offline: verifies local CLIP model/vocabulary/merges bytes and configured
# Ollama identity format; does not send a request or start the app.

# Only when the local Ollama instance or explicitly authorized bridge is up:
$inference = .\Get-PhotoIdentityApprovedInference.ps1 -VerifyOllama
# Makes GET /api/tags at a loopback origin; verifies the installed model
# against the approval. No image, video, caption or original is sent.

$inference |
    Select-Object OllamaBaseUrl, OllamaModel, OllamaVerified, CaptionContextTokens

# Example for an explicitly selected, already-authorized video frame job:
# "--clip-model", $inference.ClipModelPath
# "--clip-model-sha256", $inference.ClipModelSha256
# "--clip-vocab", $inference.ClipVocabularyPath
# "--clip-merges", $inference.ClipMergesPath
# "--ollama-model", $inference.OllamaModel
# "--ollama-sha256", $inference.OllamaSha256
# "--ollama-base-url", $inference.OllamaBaseUrl
# These do not add --apply or disclosure acknowledgements.
```

The `-AzureUpstreamOnly` mode validates just the Azure origin and can be
used from a management shell without installed CLIP/Ollama models.
The helper **does not** set any environment variables, start Ollama,
hydrate online-only media, download model files, or contact Azure. Its
`AzureOllamaUpstreamUrl` is for the operator's separate bridge command:

```powershell
# Upstream address retrieval does not require installed CLIP or Ollama.
$inference = .\Get-PhotoIdentityApprovedInference.ps1 -AzureUpstreamOnly
if (-not $inference.AzureOllamaUpstreamUrl) {
    throw "No operator-authorized Azure upstream was configured."
}
$bridge = Join-Path $env:LOCALAPPDATA "PhotoIdentity\Start-AzureOllamaProxy.ps1"
& $bridge -UpstreamBaseUrl $inference.AzureOllamaUpstreamUrl
# In another shell, verify the bridge with the helper's -VerifyOllama.
```

**Security:** the endpoint may be a bridge forwarding private thumbnails to
operator-controlled Azure. Its mere presence in `launcher.json` grants no
permission to disclose personal media, and the helper does not grant that
permission. Keep the existing operator acknowledgements, loopback-only CLI
validation, Azure ingress restrictions and separate scale controls.

## Windows validation before enabling production captions

1. Make a backup of your own launcher file outside source control. Do not
   replace it with the example. Review CLIP revision/digests and the Ollama
   `/api/tags` digest before approving their values.
2. Validate launcher shape without starting the application:

   ```powershell
   .\Start-PhotoIdentity.ps1 -ValidateConfigurationOnly
   ```

3. Verify approved local files and, once the intended loopback service
   is running, its model inventory:

   ```powershell
   $inference = .\Get-PhotoIdentityApprovedInference.ps1
   $verified = .\Get-PhotoIdentityApprovedInference.ps1 -VerifyOllama
   $verified.OllamaVerified
   ```

4. Test negative paths with disposable synthetic configuration (no
   PostgreSQL server, model inference, Azure or personal media):

   ```powershell
   .\tests\PowerShell\inference-launcher.tests.ps1
   ```

5. From the PR branch run:

   ```powershell
   dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameSelectedOneShotTests|FullyQualifiedName~PhotoCaptionEnrichmentContractTests"
   dotnet run --project tools/PhotoIdentity.Docs -- validate
   dotnet run --project tools/PhotoIdentity.Docs -- generate --check
   ```

6. With caption enrichment **still disabled**, restart the API and inspect
   Settings. Confirm pinned hashes match the model intended to serve before
   deliberately enabling photo caption work or running an explicitly
   acknowledged frame one-shot. No live caption-generation run is required
   to verify configuration plumbing.

**Limitations:** The launcher itself validates pin *syntax* at startup.
CLIP file bytes are checked before the API initializes CLIP and by the
PowerShell resolver; the Ollama inventory is checked before the API caption
worker sends a request or when the resolver runs `-VerifyOllama`.
The video CLIP/FFmpeg/lease preflight checks remain in place. Existing
historical benchmark/runbook constants are evidence, not authoritative
live-configuration defaults. Changes to model/prompt/sampling identities
still require reviewed processing and provenance decisions.
