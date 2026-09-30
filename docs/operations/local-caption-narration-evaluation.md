# Local caption and narration evaluation

This runbook covers the bounded WI-0128 experiment for deciding whether local image captions add enough value to Creative Collections to justify any future generative-model integration.

The experiment is optional and read-only. Local loopback inference remains the default; [ADR-0011](../decisions/ADR-0011-operator-authorized-remote-caption-inference.md) permits explicit Remote HTTPS thumbnail inference:

- it reads one saved Smart Collection from PostgreSQL;
- it samples only photos already selected by the existing metadata-first Creative selector;
- Local sends existing durable review-proxy bytes or an in-memory thumbnail to loopback Ollama; Remote sends only the in-memory bounded thumbnail;
- it compares deterministic catalogue-derived text with one local vision-model caption per sampled photo;
- generated text stays in memory and the private review artifact only;
- the aggregate JSON report contains no captions, filenames, Smart Collection names or revision IDs; and
- it performs zero catalogue writes.

## Local model candidate

The initial practical candidate is `qwen2.5vl:3b` through Ollama. The Ollama package is a vision-capable Q4_K_M model of roughly 3.2 GB and is published under Apache 2.0. The experiment does not bundle it into Photo Identity.

Model page:

https://ollama.com/library/qwen2.5vl:3b

Ollama vision API documentation:

https://docs.ollama.com/capabilities/vision

The evaluator records the exact locally installed model digest and byte size returned by Ollama. The tag name alone is not treated as sufficient provenance.

## Prepare Ollama and the model

Install Ollama for Windows if it is not already installed, then run:

~~~powershell
$captionModel = .\models\Get-WI0128CaptionModel.ps1
~~~

The helper explicitly runs `ollama pull qwen2.5vl:3b`, confirms the model is present in the local Ollama inventory, and prints the exact digest, package size, family, parameter size and quantization.

The evaluator itself does **not** pull models or download anything. It refuses non-loopback Ollama URLs unless `--inference-mode Remote` is explicitly selected. The model helper remains local-only.

## Safety boundary

The versioned prompt asks for exactly one short neutral sentence describing only directly visible people, objects, setting and actions. It explicitly forbids guessing:

- person names;
- relationships;
- ages, occupations, nationality or emotion;
- exact locations;
- dates or years;
- holidays, events, ceremonies, celebrations or occasions; and
- event categories such as wedding, birthday, concert, party, graduation or festival.

`GeneratedCreativeTextGuard` independently scans the returned caption for relationship, event, date/year, age and possible proper-name/location claims. A flagged caption remains unsafe derived output and must not be treated as a fact or exposed as a displayable caption. Production enrichment may retain blocked raw text internally with its risk flags so guard behavior can be diagnosed and corrected without losing evidence.

The guard is intentionally conservative. A false-positive guard flag is preferable to silently accepting an unsupported family-history claim.

## Deterministic comparison

The deterministic side uses only information already available to Creative Collections:

- direct-anchor versus contextual-addition provenance;
- known capture date when present; and
- the number of identified people, without exposing their names.

For example:

~~~text
Direct collection match · captured 2026-09-20 · 2 identified people recorded.
~~~

This is deliberately plain. The experiment asks whether local generated captions add enough useful visible detail to justify their extra runtime and model package.

## Run the bounded private experiment

Use the same representative Smart Collection and review-proxy setup used for the other M26 experiments where practical.

~~~powershell
$env:PHOTOIDENTITY_NARRATION_TEST = $env:PHOTOIDENTITY_POSTGRES_CONNECTION_STRING

if ([string]::IsNullOrWhiteSpace($env:PHOTOIDENTITY_NARRATION_TEST)) {
    $env:PHOTOIDENTITY_NARRATION_TEST =
        [Environment]::GetEnvironmentVariable(
            "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING",
            "User")
}

$proxyRoot = Join-Path $env:LOCALAPPDATA "PhotoIdentity\review-proxies"
$collectionId = "<saved Smart Collection GUID>"

dotnet run --project src/PhotoIdentity.Cli -c Release -- narration evaluate `
  --postgres-connection-env PHOTOIDENTITY_NARRATION_TEST `
  --collection $collectionId `
  --proxy-root $proxyRoot `
  --proxy-profile "jpeg-1600-q78" `
  --ollama-base-url "http://127.0.0.1:11434" `
  --model $captionModel.Model `
  --target-count 50 `
  --sample-count 12 `
  --timeout-seconds 180 `
  --report artifacts/wi-0128-caption-report.json `
  --review-output "$env:TEMP\PhotoIdentity\WI-0128-review"
~~~

Open the private review page:

~~~powershell
Start-Process "$env:TEMP\PhotoIdentity\WI-0128-review\index.html"
~~~

## What the report measures

The privacy-safe JSON report records:

- candidate, selected and sampled counts;
- generated-caption, unavailable-proxy and generation-failure counts;
- exact Ollama model name, digest, family, parameter size, quantization and package bytes;
- prompt and deterministic-template versions;
- average/median/p95 client and Ollama-reported generation runtime;
- average prompt/generated token counts;
- guard pass/flag counts by risk category;
- `GeneratedTextPersisted=false`;
- `ExternalPhotoUploads=false` in Local mode, `true` in Remote mode (bounded thumbnails only);
- inference mode and endpoint host (no URL paths, credentials or query strings); and
- `CatalogueWrites=0`.

Caption text itself is intentionally excluded from the report.

## Maintainer review

For each sampled photo, compare the image, deterministic text and generated caption. Record:

1. **Usefulness:** is the generated caption meaningfully more useful or story-like than the deterministic text?
2. **Factual correctness:** is anything visibly wrong, misleading or overconfident?
3. **Guard behavior:** did a risky unsupported claim pass the guard, or did the guard block harmless text?
4. **Repetition/style:** are captions varied and natural enough to add value, or generic/repetitive?

A positive result requires more than pleasant wording. Generated captions should add useful visible context with a low factual-error rate and acceptable local runtime/package cost.

A no-go is valid if deterministic text is sufficient, captions hallucinate too often, or the local model/runtime cost is disproportionate to the slideshow value.

## Production boundary

Even if the experiment is positive, production narration must remain optional during playback and generated text must remain regenerable derived evidence with exact model/prompt provenance. It must never become canonical photo metadata.


## Performance fallback probe

If the baseline 1600-pixel proxy run is too slow, do not create a new durable proxy profile just for the experiment. The evaluator can derive a temporary 480x320 JPEG thumbnail in memory and request a smaller Ollama context:

~~~powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- narration evaluate `
  --postgres-connection-env PHOTOIDENTITY_NARRATION_TEST `
  --collection $collectionId `
  --proxy-root $proxyRoot `
  --proxy-profile "jpeg-1600-q78" `
  --ollama-base-url $captionModel.BaseUrl `
  --model $captionModel.Model `
  --caption-image-mode thumbnail `
  --ollama-context 1024 `
  --target-count 50 `
  --sample-count 1 `
  --timeout-seconds 600
~~~

This is a performance/feasibility probe, not a production default. The report records both the caption image mode and requested Ollama context so results from the full review proxy and temporary thumbnail are not conflated.

If the thumbnail/context probe materially improves runtime, repeat a small qualitative sample before deciding whether the reduced visual detail is still good enough. If it remains measured in minutes per caption, record the local generative path as impractical on the maintainer hardware rather than spending time on a full 12-photo review.


## Retained archive caption enrichment

The positive WI-0128 result is retained as optional **photo enrichment**. Production generation is independent of slideshows and Smart Collections:

- **Settings → Automatic photo captions** is a durable server-side setting and defaults to off.
- Caption generation language can be **Svenska** or **English**. The persisted product default for a previously unconfigured catalogue remains Swedish, but the maintainer's current operating convention is **English** so caption and Visual/CLIP searches can use the same language.
- Changing the active generation language does not rewrite older evidence. Swedish and English caption rows may coexist for the same immutable revision, and search can continue to find older Swedish text when queried in Swedish.
- When enabled, one background worker gradually selects current photo revisions that already have the configured durable review proxy and lack caption evidence for the active generation policy.
- Only the configured durable review proxy is opened. Photo Identity derives a temporary 480x320 JPEG in memory before model inference.
- The Ollama endpoint is loopback-only by default. Operator-configured Remote mode requires HTTPS under ADR-0011.
- Generation is serial and continues independently of what the user views. Opening a photo, Smart Collection or slideshow never queues caption work.
- Guard-passing and guard-blocked results are persisted as versioned revision-bound derived evidence with model/prompt/image-mode/context provenance.
- Guard-blocked output is not exposed as a displayable caption.
- Generated captions never become canonical catalogue metadata.
- Photo details, slideshow presentation and future query/search features may read the persisted evidence without owning its lifecycle.

The default product generation configuration reuses the successful probe:

~~~text
Model: qwen2.5vl:3b
Ollama endpoint: http://127.0.0.1:11434/
Image mode: temporary 480x320 thumbnail
Requested context: 1024
Request timeout: 600 seconds
~~~

Optional runtime configuration keys are:

~~~text
PhotoIdentity:CaptionEnrichment:InferenceMode   # Local (default) or Remote
PhotoIdentity:CaptionEnrichment:OllamaBaseUrl
PhotoIdentity:CaptionEnrichment:Model
PhotoIdentity:CaptionEnrichment:ContextTokens
PhotoIdentity:CaptionEnrichment:TimeoutSeconds
~~~

Local requires an absolute loopback HTTP(S) address. Remote requires explicit mode and an explicit absolute HTTPS address. Both modes reject URL credentials, query strings and fragments; redirects are disabled. Conflicting current and legacy endpoint settings fail startup. Ollama/model installation remains an explicit operator action; enabling archive enrichment does not download a model.

### Accepted private evidence

On 2026-09-20:

- one full 1600-pixel proxy caption took 373,568.2 ms;
- the first 480x320 thumbnail probe took 118,081.4 ms;
- a four-photo thumbnail sample averaged 95,858.8 ms per caption;
- all 4 captions generated successfully;
- the maintainer judged all 4 useful;
- no factual errors were observed in the four-photo review;
- all 4 captions passed the guard; and
- the qwen2.5vl:3b package reported 3,200,627,168 bytes.

The production decision is therefore **useful but latency-constrained**: accumulate captions gradually and reuse them, while leaving model/runtime optimization for later.

### Production quality follow-up

Maintainer sampling on 2026-09-22 inspected the 30 most recent Swedish `wi-0128-photo-caption-v2` rows.

- All 30 rows had empty risk flags, confirming the sentence-boundary proper-name/location correction removed the observed false-positive pattern.
- The model did not reliably follow the prompt's output-shape instruction: many captions contained multiple sentences even though exactly one sentence was requested.
- Several stored outputs ended mid-word or mid-sentence, showing that a token-bounded raw model response is not itself a safe display contract.
- Some captions contained awkward Swedish wording. Language quality remains model-dependent and is distinct from the factual-claim guard.

The corrective production contract is therefore stronger than the prompt alone. Generation policy `wi-0128-photo-caption-v3` deterministically collapses whitespace, keeps the first complete sentence and enforces at most 20 words before claim-guard evaluation and persistence. If the first sentence exceeds the bound, it may be shortened only at a clause boundary inside the limit. Output that cannot produce a complete bounded sentence is retained internally with `caption-output-format` and is not displayable. Retained v1/v2 captions are normalized and promoted locally where possible so archive-wide correction does not require another vision-model pass. WI-0128 remains in progress until this behavior is confirmed with a fresh production sample.


## Temporary remote caption backlog procedure (WI-0174)

This procedure is provider-neutral. It does not provision infrastructure. The maintained Windows host runs Photo Identity, resolves catalogue candidates and review proxies, renders thumbnails, normalizes/guards output and writes PostgreSQL. The remote endpoint never receives database access or archive access.

### Prerequisites and privacy

Prepare an operator-controlled Ollama-compatible HTTPS endpoint with a valid trusted TLS certificate, `/api/tags` and `/api/chat`, and the selected vision model already installed. Model installation is independent of Photo Identity. Provide independent endpoint access controls, such as a network allow-list or hosting-platform authentication compatible with the client. Photo Identity does not implement credential headers in this contract: endpoints needing a client bearer header are not supported here. Never embed credentials/tokens in the URL. URLs with user information, queries or fragments are rejected. Routing base paths are supported and normalized with a trailing slash. Redirects are rejected rather than followed; use the final endpoint directly.

Remote mode discloses a locally rendered 480x320 JPEG depicting the photo, the neutral prompt, model name and Ollama protocol/generation options. It strips source EXIF by decoding and re-encoding the review proxy. Original files/bytes, review-proxy/source paths, identities/assignments, face crops/embeddings, Places/GPS, dates, collection membership and connection information are not serialized. People and locations visible in the image are still visible to the endpoint. Choose a trusted endpoint and its retention policy accordingly. The private evaluator review page stays on the Windows machine; do not publish it.

### Verify exact model identity and benchmark first

Leave automatic enrichment disabled in Settings while preparing the run. Record the local model inventory, then compare the remote inventory before switching:

~~~powershell
$remoteCaptionEndpoint = "https://<operator-controlled-host>/"
$modelName = "qwen2.5vl:3b"
$localModel = (Invoke-RestMethod "http://127.0.0.1:11434/api/tags").models |
    Where-Object { $_.name -eq $modelName }
$remoteModel = (Invoke-RestMethod ($remoteCaptionEndpoint.TrimEnd('/') + "/api/tags")).models |
    Where-Object { $_.name -eq $modelName }
$localModel | Select-Object name, digest
$remoteModel | Select-Object name, digest
~~~

Use a final HTTPS endpoint; Photo Identity does not follow redirects. Compare the full normalized SHA-256 digests, not just model tags. The evaluator and worker resolve and record the actual digest themselves. A different digest is a different model policy candidate; do not describe it as identical. For a performance comparison, run Local and Remote with the same collection, thumbnail mode, model digest, sample size and context:

~~~powershell
$proxyRoot = Join-Path $env:LOCALAPPDATA "PhotoIdentity\review-proxies"
$collectionId = "<saved Smart Collection GUID>"
$env:PHOTOIDENTITY_NARRATION_TEST = $env:PHOTOIDENTITY_POSTGRES_CONNECTION_STRING

dotnet run --project src/PhotoIdentity.Cli -c Release -- narration evaluate `
  --postgres-connection-env PHOTOIDENTITY_NARRATION_TEST `
  --collection $collectionId --proxy-root $proxyRoot --proxy-profile jpeg-1600-q78 `
  --inference-mode Local --ollama-base-url http://127.0.0.1:11434/ `
  --model $modelName --caption-image-mode thumbnail --ollama-context 1024 `
  --sample-count 4 --timeout-seconds 600 --report artifacts/caption-local.json

dotnet run --project src/PhotoIdentity.Cli -c Release -- narration evaluate `
  --postgres-connection-env PHOTOIDENTITY_NARRATION_TEST `
  --collection $collectionId --proxy-root $proxyRoot --proxy-profile jpeg-1600-q78 `
  --inference-mode Remote --ollama-base-url $remoteCaptionEndpoint `
  --model $modelName --caption-image-mode thumbnail --ollama-context 1024 `
  --sample-count 4 --timeout-seconds 600 --report artifacts/caption-remote.json `
  --review-output "$env:TEMP\PhotoIdentity\remote-caption-review"
~~~

Remote defaults to thumbnail mode and rejects explicit proxy mode. Reports record `InferenceMode`, `EndpointHost`, exact digest, image mode, context, quality-guard counts and timing, with `ExternalPhotoUploads=true` for Remote. The evaluator remains read-only and uses its existing experiment prompt; compare evaluator runs to each other rather than assuming evaluator and production prompt versions are interchangeable. Review the small qualitative sample before enabling archive enrichment.

### Opt in for the worker

Stop Photo Identity. Set deployment configuration in the PowerShell session that launches it, preserving the same model/context/language/prompt policy:

~~~powershell
$env:PhotoIdentity__CaptionEnrichment__InferenceMode = "Remote"
$env:PhotoIdentity__CaptionEnrichment__OllamaBaseUrl = $remoteCaptionEndpoint
$env:PhotoIdentity__CaptionEnrichment__Model = $modelName
$env:PhotoIdentity__CaptionEnrichment__ContextTokens = "1024"
$env:PhotoIdentity__CaptionEnrichment__TimeoutSeconds = "600"
& ".\.artifacts\packages\PhotoIdentity-win-x64\PhotoIdentity.cmd"
~~~

If deployment JSON already specifies these keys, update them there or ensure these process environment overrides apply. Remove any conflicting `PhotoIdentity:GeneratedCaptions:OllamaBaseUrl` legacy setting. Endpoint selection is operator configuration and is not editable from the browser. In Settings, confirm Remote, endpoint host and configured model; enable the desired language and watch the resolved digest, worker state/message and the remote-processing notice. The worker remains serial; a failure waits five minutes before retry and never falls back to Local. Disable enrichment after a small batch first and inspect stored caption provenance/guards/timing before letting the backlog continue.

~~~powershell
$Api = "http://127.0.0.1:5080"
Invoke-RestMethod "$Api/api/caption-enrichment/status" |
    Format-List inferenceMode, endpointHost, model, modelDigest, state, message, nextAttemptAtUtc
~~~

### Return to normal Local processing

Disable enrichment and stop Photo Identity. Restore the local endpoint and restart from the same configuration source/session:

~~~powershell
$env:PhotoIdentity__CaptionEnrichment__InferenceMode = "Local"
$env:PhotoIdentity__CaptionEnrichment__OllamaBaseUrl = "http://127.0.0.1:11434/"
& ".\.artifacts\packages\PhotoIdentity-win-x64\PhotoIdentity.cmd"
~~~

Confirm Local in Settings/status, verify the same model digest, then re-enable enrichment as needed. No database migration or caption rewrite is required. Matching model name/digest, language, generation/prompt version, thumbnail mode and context reuse remote evidence; changing transport alone does not regenerate it. A changed digest/policy retains the normal existing candidate behavior. Remove temporary endpoint capacity/access separately using the hosting platform's own process.

### Maintained-machine acceptance record

Record the small remote benchmark and batch size; full local/remote model digests; policy/language/context; representative guard and timing results; sanitized failure/backoff observations; then Local restart status and evidence that the same remotely completed revisions were not generated again. This acceptance is pending until performed on the maintained catalogue; technical endpoint feasibility from the issue is not application acceptance evidence.
