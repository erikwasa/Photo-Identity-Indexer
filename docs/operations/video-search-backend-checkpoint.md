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

## Maintained-host browser and poster follow-up (2026-11-02)

After PR #713 merged with passing Windows CI, the maintainer confirmed
the Video Search page works with stored captions, matching-frame time
and generated-evidence labels. One result showed a poster and two
newer results displayed **Poster unavailable**. The user suggested
they may have been imported from another folder; this is a hypothesis,
not a confirmed fault.

The read-only video-search API uses durable FrameEvidence, not still
poster metadata. Still posters are generated only by the separate
opt-in `video posters` CLI and require an existing registered source
root, a supported `bt709` revision, already-local original bytes
and a verified independent derivative root. The command traverses
nested folders under the selected source root, but does not process
another registered root automatically. Search does not attempt
poster generation or source hydration.

**Diagnose before generating:** Query `/api/videos?offset=0&limit=60`
(page while `hasMore=true`) and compare the missing search revisions
with the library's `hasPoster` flag. Separately GET the existing
`/api/videos/{revisionId}/poster` route. Keep private IDs off PR logs.

- `hasPoster=false` and poster 404: no current poster evidence row;
  frame evidence and search are independent and can still be correct.
- `hasPoster=true` and poster 404: a row exists but the approved poster
  file/root, checksum, access or current-revision check failed. Check
  the existing approved `PhotoIdentity__VideoPosterRoot`; do not
  change it, overwrite rows or regenerate blindly.
- `hasPoster=true` and poster 200: the server can deliver a JPEG;
  inspect the browser request and UI before touching files.
- Search revision absent from the video library: investigate an
  eligibility inconsistency rather than running poster materialization.

If no poster record is present, choose the **verified registered**
source root containing those already-local videos, not an arbitrary
subfolder. Read and use the dry-run-only `video posters --enable-video`
command in [WI-0151 durable poster instructions](video-durable-posters.md).
Use the approved FFmpeg path from `Get-PhotoIdentityApprovedVideoTools.ps1`
and the exact existing launcher poster root and confirmed database.
`--max-videos` counts the first eligible candidates in traversal
order, **not selected revision IDs**; an arbitrary bounded run may
not include the two search results. Require a reviewed preview and
normal backup/authorization before any separately deliberate `--apply`.
Do not re-run FrameEvidence, hydrate OneDrive placeholders or initiate
new Azure processing solely because a poster is absent.
## Checkpoint 3 — eligibility regression without local media

After the next PR's Windows CI, the repository-level
`PostgresVideoFrameEvidencePublicationTests` verifies that Search suppresses
unsupported/HDR, deleted, superseded, non-local and incomplete synthetic
FrameEvidence, in addition to its existing exclusion/purge test. This
uses a **disposable** test PostgreSQL database only.

```powershell
dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameEvidencePublicationTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

The test class now matches the maintained `Postgres` CI filter and
the PR classifier explicitly activates the disposable Windows PostgreSQL
lane when this test-only file changes. The CI runner provides an isolated
test-admin connection; no production catalogue is used.
A local run executes the database-backed case only if
`PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` is explicitly
configured for a **disposable admin connection**; otherwise that test
returns early. Do not point an admin test connection at the maintained
production catalogue, and do not create new credentials just for this
workitem when the CI disposable database test passes.

The maintainer is deferring the two missing posters. No poster dry run,
`--apply`, database backup, original-video scan, or rerun of frame
inference is required for WI-0191 checkpoint 3.

## Checkpoint 4 — photo-only preservation after video evidence publication

The `PostgresPhotoSearchRepositoryTests` disposable database fixture
now publishes valid synthetic video frame captions/embeddings using
the video job/publisher path. Photo search query order, ranked/component
scores, indexed/caption totals and an existing saved photo-list
collection must remain exactly the same. The video stays searchable
through the separate Video Search repository and cannot be saved
through the photo collection adapter. CI exercises this fixture
without any private user media.

After this PR merges, optionally check the maintained running app with
a private photo query known to match a caption and a video query known
to match existing frames:

```powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main
.\Test-PhotoIdentityVideoSearchIsolation.ps1
```

The script prompts privately for one photo and one video phrase,
runs Photo / Video / Photo queries and two read-only saved-collection
GET requests, and prints **only** result counts, equality flags and
individual elapsed milliseconds. Pass means photo membership/order,
component scores, photo index totals and saved photo collection
membership were unchanged **across the intervening video search
request**. It does not write photos or collections, access video
originals, trigger processing or output source identifiers.
A successful pair of queries does not prove the photo baseline latency
of an earlier installed version; inspect timings only if a slowdown
is actually observed.

The corresponding automated test is already included in the existing
Windows PostgreSQL CI package-verification lane; optional local
disposable-db execution is:

```powershell
dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj -c Release --filter "FullyQualifiedName~PostgresPhotoSearchRepositoryTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

The database test requires the existing
`PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` configured
for a disposable PostgreSQL test environment; without it, the
database-backed test returns early. **Never point this admin test
variable at the maintained catalogue.** No local DB rerun is required
if the full CI disposable test passes. Missing posters remain deferred.

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
- The separate Video Search browser page was accepted for basic use on the
  maintained host; two newer search results lack durable still posters.
  Poster diagnosis, larger-set relevance, negative-case eligibility and
  photo-ranking/performance comparisons remain outstanding. No video
  collection integration or unattended processing is introduced.
