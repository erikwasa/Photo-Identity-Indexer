# WI-0191 checkpoint 1 — video-search backend verification

This is a deliberately bounded first checkpoint, not full WI-0191 acceptance.
It adds the separately selected **video-only** POST endpoint
`/api/video-search/query` and leaves `/api/photo-search/query` unchanged.
The new route is unavailable (404) unless
`PhotoIdentity:VideoLibraryEnabled=true` is already explicitly configured.
Do not enable processing, source hydration or Azure inference to test search.

## Automated tests and documentation

From a current main-derived checkout on Windows:

```powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj -c Release --filter "FullyQualifiedName~VideoSearchRankerTests"
dotnet build PhotoIdentity.slnx -c Release
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

A failing build or generator check is a blocker, not evidence of acceptance.
No local Windows command has been claimed to run through the GitHub connector.

## Bounded maintained-host pilot

Only after the PR has passed CI, launch using the existing approved local
PostgreSQL catalogue, installed pinned CLIP model and current personal
video-library configuration. Do not edit credentials or switch to a
disposable database casually. The route is explicitly video-only.

1. With Video Library disabled, confirm the endpoint responds with 404.
   No current photo-search behavior should change.
2. With Video Library explicitly enabled, run **Caption** mode on a harmless
   text query that corresponds to an already published frame. This does not
   need a loaded CLIP model. Inspect the result **privately**; captions are
   personal data and should not be copied into PR comments.
3. Run **Semantic** and **Combined** with an approved local CLIP model.
   The server authenticates the model through the same existing configuration
   as photo search and ignores vectors with differing digest/preprocessing.
   An unavailable model produces no Visual matches; Combined can still use
   matching captions.
4. Compare returned revision IDs to the existing privately audited current
   FrameEvidence jobs. Observe one result per revision, a real frame
   timestamp, generated-evidence provenance and no unsupported/excluded or
   superseded revision. Do not change real source exclusions merely to test
   the negative case; use disposable PostgreSQL fixtures later.
5. Compare one unchanged photo search query before and after the change.
   Do not infer performance equivalence from a single query.

Example call, run only on the existing local app port:

```powershell
$body = @{ query = "a harmless scene description"; mode = "caption"; limit = 10 } | ConvertTo-Json
$result = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:5080/api/video-search/query" -ContentType "application/json" -Body $body
$result | Select-Object Mode, SemanticModelAvailable, CandidateVideos, CandidateLimitReached
$result.Items | Select-Object TimestampMilliseconds, Sources, EvidenceKind
```

Keep actual captions, source identifiers and media content local. Do not
paste personal search results into public CI logs or issue comments.

## Observed Windows maintainer smoke (2026-11-02)

PR #711 merged; its final Windows build/integration/package run
[#3398](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/41426752736)
passed. Against the maintained local catalogue, the maintainer reported:

| Query mode | Candidate videos | Returned videos | Unique revisions | Model available | Elapsed |
| --- | ---: | ---: | --- | --- | ---: |
| Caption | 3 | 3 | yes | false (expected) | 32.3 ms |
| Semantic | 3 | 3 | yes | true | 77.2 ms |
| Combined | 3 | 3 | yes | true | 59.9 ms |

No mode reached the 256-candidate bound. The standalone CLIP approval
script confirmed local model-file availability; the Combined response
included timestamped, model-labeled, source-labeled generated frame
evidence and revision-bound poster paths. This **does not establish
caption correctness, large-set relevance, poster bytes on disk, or
absence of stale/excluded results across untested cases**.

The initial photo Caption API request returned zero results. A
subsequent privately known-positive Caption query returned **10 of 10
requested photos**, with **26,904 indexed photos** and **24,770
displayable captions** reported by the API. Photo Caption retrieval
therefore functions on the maintained host after the new backend was
merged. This is a focused positive smoke, **not proof** that photo
ranking, eligibility, saved results or baseline latency are unchanged;
there is no before/after comparison or other-mode control here.

The maintainer has not yet reported focused local tests or documentation
validation for this checkpoint. No private queries or media identifiers
are stored in this record.

## Dedicated browser checkpoint — manual UI verification

With the new WI-0191 browser PR installed from current `main` after
merge (the existing published app/package must also be refreshed):

1. Keep the same approved launcher, local PostgreSQL database, and
   `PhotoIdentity__VideoLibraryEnabled=true`; restart the installed
   app after deploying new code. There is no need to run processing,
   hydrate originals, use the Azure bridge or republish frame evidence.
2. Open `http://127.0.0.1:5080/video-search` (also linked under
   **Advanced → Video search** and from **Videos**).
3. Enter a private scene phrase known to match an indexed video.
   Check **Captions only**, **Visual only** and **Visual + captions**.
   Confirm matching captions, one result per video, frame timestamp,
   evidence badges, and expandable generated-evidence policy/model
   provenance. Check loading and zero-result states with a mismatching
   harmless query; keep private captions off screenshots/reports.
4. Check poster images. A missing/evicted poster should show
   **Poster unavailable** rather than launching processing. Clicking
   **Browse video library** returns to existing video cards.
5. If the launcher is already configured with Video Library off, the
   page should show a disabled message and no input controls. Do **not**
   switch off the real maintained installation just to run this case.
   The backend continues to enforce its own disabled 404 gate.
6. On a narrow desktop/mobile-width browser, verify that the form and
   result cards remain readable and keyboard Enter performs a query.
7. Run one known-positive `/search` photo Caption query (previously
   observed 10/10 on the maintained catalogue). Photo-only selection
   and saved photo slideshow collections remain unchanged; do not
   create a new saved collection solely for this smoke.

From the repository root, source checks for this checkpoint are:

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoSearchPresentationTests"
dotnet build PhotoIdentity.slnx -c Release
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

These commands are not a substitute for checking the built app after
deployment. The previously observed three-video query timings cannot
establish relevance or large-library performance.

## Limits intentionally retained for the next checkpoint

- Current reader scans a maximum of **256** most-recent, current eligible
  video revisions (not an exhaustive whole-library index). A
  `candidateLimitReached` response flag reports possible truncation.
- Simple case-insensitive token/phrase caption ranking is a baseline, not
  PostgreSQL full-text ranking. Combined mode fuses per-video ranked lists,
  not raw photo/video scores. The displayed timestamp is always from the
  selected stored frame.
- Access is verified from catalogue status and current revision/exclusion
  at read-snapshot time. This initial API does not confirm on-disk poster
  or playable-proxy presence for each result; poster bytes remain guarded
  separately by the existing video endpoint.
- A separate Video Search browser page is now in checkpoint-2 development,
  pending build and manual UI acceptance. There is still no video collection
  integration or unattended processing. Poster availability, maintained
  browser verification, larger-set relevance and negative-case integration
  tests remain pending.
