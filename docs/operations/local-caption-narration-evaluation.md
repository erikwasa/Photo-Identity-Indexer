# Local caption and narration evaluation

This runbook covers the bounded WI-0128 experiment for deciding whether local image captions add enough value to Creative Collections to justify any future generative-model integration.

The experiment is deliberately local, optional and read-only:

- it reads one saved Smart Collection from PostgreSQL;
- it samples only photos already selected by the existing metadata-first Creative selector;
- it sends only existing durable review-proxy bytes to an Ollama endpoint that is required to be loopback;
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

The evaluator itself does **not** pull models or download anything. It refuses non-loopback Ollama URLs.

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
- `ExternalPhotoUploads=false`; and
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
- Caption generation language can be **Svenska** or **English**; Swedish is the default.
- When enabled, one background worker gradually selects current photo revisions that already have the configured durable review proxy and lack caption evidence for the active generation policy.
- Only the configured durable review proxy is opened. Photo Identity derives a temporary 480x320 JPEG in memory before model inference.
- The Ollama endpoint remains loopback-only.
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
PhotoIdentity:CaptionEnrichment:OllamaBaseUrl
PhotoIdentity:CaptionEnrichment:Model
PhotoIdentity:CaptionEnrichment:ContextTokens
PhotoIdentity:CaptionEnrichment:TimeoutSeconds
~~~

The configured Ollama URL is rejected unless it is an absolute loopback HTTP(S) address. Ollama/model installation remains an explicit operator action; enabling archive enrichment does not download a model.

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
