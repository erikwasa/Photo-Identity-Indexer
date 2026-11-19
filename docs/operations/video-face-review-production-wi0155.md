# WI-0155 production video-face review storage and admission

**Status:** PR #828 draft, migration v42. This is a production persistence
boundary, not an enabled video-face recognition job. Automatic identity
assignment is not authorized. The historical V06 package/CSV remains
private source-bound *experimental* review and is not imported into the
new tables by this PR.

## Data flow and authority

```mermaid
flowchart TD
    A["Future guarded video worker, source revision lease"] --> B["Bounded native frame selection + exact YuNet / SFace model"]
    B --> C["Production all-confirmed-exemplar per-face matcher (not implemented)"]
    C --> D["Internal PublishAsync: exact current video revision + SHA + model provenance"]
    D --> E["PostgreSQL video_face_evidence_batches / observations / suggestions"]
    E --> F["Opt-in GET review API with current canonical names"]
    F --> G["Human confirms / unknown / not-face / cannot-tell / clear"]
    G --> H["Append-only video_face_review_actions, source-fenced"]
    H -. "future explicit human acceptance" .-> I["video_person_actions / presence (not implemented)"]
    J["Source exclusion / changed revision"] --> K["Current reads refuse stale or excluded evidence"]
    J --> L["Exclusion trigger purges batches + dependent review actions"]
```

The publication endpoint is **not** exposed over HTTP. The producer
contract accepts derived **geometry, timestamp and scored person UUIDs**,
not images, vectors, external paths, model files, source media or a
caller-generated person list. A future guarded worker must prove that
its frame extraction and embedding inputs are from an admitted current
video revision, and use production exemplar scoring before invoking it.

### PostgreSQL schema and invariant summary

- Version 42 creates `video_face_evidence_batches`,
  `video_face_observations`, `video_face_suggestions` and
  `video_face_review_actions`. Foreign keys ensure video-only
  revisions, canonical person records and transactional cascade
  deletion of derived observations.
- One immutable batch per video revision. A SHA-256 fingerprint of the
  exact evidence payload makes retries idempotent and rejects conflicting
  evidence instead of replacing already-reviewed face IDs. Model upgrades
  and intentional reruns require a separately designed versioned
  replacement/retention workflow; this PR does **not** overwrite history.
- The writer takes the source-row lock and validates the revision is
  latest, included, supported, local-folder and byte-digest matched.
  PostgreSQL coordinates exclusion FK locks with that source lock.
  Reads repeat the currentness/exclusion predicate.
- Individual observations have native frame dimensions, normalized
  face boxes, frame ordinal and timestamp. Person suggestions have
  ranked score and canonical person ID; live names are resolved at read
  time and merged people are marked non-selectable. No original video,
  aligned face crop, raw frame, embedding or local file path is stored.
- Human actions are append-only, identified by immutable observation
  UUID, revision and active canonical person; identical repeated
  actions do not append duplicates. `clear` reverses the current
  decision but keeps the audit trail. **Neither a human confirmation
  nor its score writes video-person presence or photo identity.**
- Source-exclusion insertion deletes the batch, which cascades to
  suggestions and review history. New revisions invalidate all
  current-evidence reads for older revisions even before cleanup.

### API routes (both disabled by default)

Enable only with both settings:

```text
PhotoIdentity:VideoLibraryEnabled=true
PhotoIdentity:VideoFaceReviewEnabled=true
```

GET `/api/videos/{revisionId}/face-review` returns only current
published evidence and current canonical person display names.
POST `/api/videos/{revisionId}/face-review/{observationId}/decision`
takes JSON `{"decision":"known","personId":"<canonical UUID>"}`.
Other decisions `unknown`, `not-face`, `cannot-tell` and `clear`
must omit `personId`. The POST returns `{"changed":true|false}`.
The endpoints provide no way to upload evidence or trigger inference.
The feature gate defaults to false. No UI is connected until a guarded
production publisher and private crop renderer have been implemented.

### Safe verification (PowerShell 7.6, disposable PostgreSQL only)

From the repository root on the PR branch:

```powershell
dotnet build src/PhotoIdentity.Persistence.Postgres/PhotoIdentity.Persistence.Postgres.csproj -c Release
dotnet build src/PhotoIdentity.Api/PhotoIdentity.Api.csproj -c Release
# Set ONLY to a disposable PostgreSQL administrator URL for test DB creation:
if (-not $env:PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING) {
    throw 'The isolated PostgreSQL test requires a disposable admin connection.'
}
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj `
    -c Release --filter FullyQualifiedName~VideoFaceReviewProductionPersistenceTests
```

The test creates and drops its own uniquely named database. It seeds
synthetic video revisions and fake people (never originals/production
catalogue), verifies retry, conflicts, human undo, excluded source
cascade, stale revision and **zero** photo or video-person tagging.
If the admin connection environment variable is unset the test
**skips its body**, so a green test command alone does not prove DB
verification.

**Deployment caution:** an application started against the real
production catalogue will apply migration v42 during initialization
even though face-review routes and inference are disabled. Take and
verify a PostgreSQL backup and inspect the new schema and restoration
plan before deploying or testing this branch against production.
Do not enable the new feature or attempt to import experimental V06
JSON/CSV as production evidence. No new video processing settings or
worker activation is added in this PR.

## Next product integration slices

1. Guarded frame producer integrated into the existing video
   advancement/lease path, with bounds and native face pixel geometry.
2. Exact YuNet/SFace model provenance and source-bound crop access;
   shared production full-exemplar person scorer (not group ranking).
3. Face-review UI backed by these APIs, current people and private
   revision-scoped crop evidence.
4. Explicit human-approved presence promotion to the existing
   `video_person_actions` audit system with deduplication, undo
   and stale-source protection.
5. Operational rights, retention, deletion and observed false-person
   risk acceptance before enabling any background recognition.
