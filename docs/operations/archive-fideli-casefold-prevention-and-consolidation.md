# Fideli archive casing — prevention and reconciliation

Status: READ-ONLY evidence and ingestion prevention. No production writes,
no PostgreSQL migration, no deletions or automatic cleanup.

## Findings and limits

- The exact-duplicate view groups verified source copies by current
  SHA-256 **within each source**. It may contain legitimately separate
  paths with identical bytes. This is not the same as a path-alias audit.
- The historical case-insensitive-path audit found 1,088 groups. The
  first five have been logically retired and verified. At the last
  fresh audit, 328 more passed the guarded eligibility check; 760 did
  not, including the five already retired (thus approximately 755
  unresolved cases).
- User-selected canonical identity: `fideli/` is ALWAYS the keeper
  when its partner has the identical relative path under `Fideli/`.
  Creation order and automatically generated tags/captions/places
  are not priorities. The two copies must still have the same source
  identity, verified SHA-256 and byte size. Manual edits on the
  capitalized asset require preservation/reconciliation before
  changing production, not leaving both images visible permanently.
- Existing archive photo batch scans already reject case-only aliases
  against persisted assets and remap active, explicitly retired aliases
  to the surviving asset. The new prevention change also rejects two
  previously unseen colliding spellings **inside the same scan batch**;
  applies the same slash normalization as retirement; and blocks new
  case aliases through the separate video-registration path. This
  prevents those known ingestion routes from creating new collisions,
  but a future database-level uniqueness design remains useful after
  resolving legacy groups.

## Disposable acceptance tests

From Windows PowerShell 7.6 in the PR branch:

~~~powershell
.\verify-postgres.ps1 -SkipContainerStart
~~~

The integration tests cover old exact-key reuse, simultaneous photo
casefold aliases, slash-vs-backslash casefold aliases and video
registration case aliases. Each failure must leave original revisions
and existing annotations intact.

## Read-only inventory of real production state

This SQL reproduces the exact-duplicate app's per-source hash grouping,
then independently counts case-folded archive paths and specifically
`fideli/` versus `Fideli/`. It emits ONLY aggregate numbers.

~~~powershell
$ErrorActionPreference = 'Stop'
$sql = Get-Content '.\tools\archive-case-duplicate-reconciliation-inventory.sql' -Raw
$result = $sql | & podman exec -i $containerId sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -h 127.0.0.1 -U "$POSTGRES_USER" -d photoidentity_main -X -q -v ON_ERROR_STOP=1'
if ($LASTEXITCODE -ne 0) { throw 'Read-only inventory failed.' }
$inventory = @($result | ConvertFrom-Csv)
if ($inventory.Count -ne 1) { throw 'Unexpected inventory shape.' }
$inventory[0] | Format-List
~~~

- `app_equivalent_verified_hash_duplicate_groups` is based on hashes.
  Compare with the app for the SAME sources and time; the SQL sums
  all sources, but never groups across source IDs.
- `historical_casefold_collision_groups` includes previously retired
  aliases. `fideli_exact_two_spellings_groups` counts literal
  lower/upper directory-spelling pairs.
- `fideli_verified_matching_hash_size_pairs` counts two-copy path
  groups with matching verified hash and bytes. This alone does not
  authorize hiding protected reviews or metadata.
- `fideli_casefold_groups_with_multiple_active_assets` counts path
  groups still presenting multiple active assets. This should reach
  zero only after every protected case has been resolved separately.
- `verified_hash_duplicate_groups_spanning_distinct_paths` demonstrates
  why a global one-row-per-SHA-256 constraint would be unsafe.

No original photo is opened by the audit and no rows are modified.
It does not require a fresh production backup.

## NEW: lowercase-canonical policy preview (read-only, source-specific)

The existing 328-candidate planner and 5-pair Apply service enforce
**older survivor, zero upper explicit history and identical effective
annotations**. Those restrictions are NOT the requested final policy,
because a second ingestion may have produced different automatic
metadata. Do not use the old plan to estimate the final number of
casing duplicates that can be consolidated.

Instead, after exporting the two read-only audit CSVs, run the new
policy assessment:

~~~powershell
$canonical = .\tools\new-archive-fideli-canonical-preview.ps1 `
    -RetirementEvidenceCsv '.\artifacts\archive-case-retirement-evidence-fresh.csv' `
    -EffectiveStateCsv '.\artifacts\archive-case-effective-state-fresh.csv'

$canonical | Select-Object PathCollisionGroupsRevalidated,
    ExactFideliSpellingPairs, CandidateLowercaseCanonical,
    OfCandidatesWithEffectiveMetadataDifferences,
    OfCandidatesNotCompatibleWithCurrentRunner,
    RequiresExplicitUpperHistoryReconciliation,
    BlockedContentProof, ExcludedOrInactive

$canonical.Classifications | Format-Table -AutoSize
~~~

- The **canonical keeper** is always the lowercase folder spelling
  (regardless of created_at_utc or which copy has more automatic data).
- The assessment only includes a two-asset, same-source normalized
  path where the literal path string differs in its FIRST character
  (fideli/ versus Fideli/); unrelated same-SHA photos are excluded.
- It requires verified hashes and byte sizes for both copies, no
  source exclusion and both still active. Differences in effective
  auto-derived metadata are reported but don't disqualify the keeper.
- All uppercase photo-tag/person edits, manual Place/capture date and
  preference changes, human/unknown face actions and labels, manual
  collection references and featured faces are flagged for explicit
  history reconciliation. Unknown actors are NOT presumed automatic.
- It marks when the current five-pair service cannot yet apply the
  requested policy due to the age or effective-state gate.
- The private output is **not a CLI manifest**; no production command
  accepts it as an Apply authorization.
- All historical rows, even on the uppercase image, remain preserved
  until an independently reviewed reconciliation mechanism exists.
  The lowercase copy's manual decisions take precedence; do not
  silently union automatic labels or override manual decisions.

The synthetic policy test is independent of PostgreSQL:

~~~powershell
pwsh -NoProfile -File '.\tests\PowerShell\archive-fideli-canonical-preview.tests.ps1'
~~~

## Offline readiness breakdown (private plan; no DB writes)

From the existing staged session, read the authoritative revalidated
candidate CSV. Report only counts, never paths or asset IDs:

~~~powershell
$sessionDir = Split-Path -Parent $session.PrivateSessionReportPath
$planCsv = Join-Path $sessionDir 'revalidated-input\retirement-candidates-private.csv'

$readiness = .\tools\get-archive-fideli-retirement-readiness.ps1 `
    -PrivatePlanCsv $planCsv

$readiness | Select-Object TotalCollisionGroups, ScopedCollisionGroups,
    EligibleUnretired, EligibleWithLowercaseSurvivorAndUppercaseSecondary,
    EligibleOtherCaseDirection, HeldOrAlreadyRetired
$readiness.HeldReasons | Format-Table -AutoSize
~~~

This distinguishes how many eligible pairs actually have the requested
older lowercase `fideli/` keeper and newer `Fideli/` secondary, and
summarizes the different blocker types in the other pairs. One group
may have multiple blockers, so reason counts are not additive.

The synthetic script test needs no PostgreSQL:

~~~powershell
pwsh -NoProfile -File '.\tests\PowerShell\archive-fideli-retirement-readiness.tests.ps1'
~~~

## Backup policy — one restore-tested checkpoint per cleanup session

We do NOT need another 19 GiB restore after every five retirements.
Before a *write session*, stop Photo Identity, make ONE fresh backup
after the first five successful retirements, and prove it restores with
`verify-postgres-backup-restore.ps1 -ApplicationStopped`. Preserve the
pre-pilot and the new recovery checkpoint until the entire session is
verified. Each independently approved five-pair Apply may reuse that
restore verification report while it is valid (currently 24h). Each
private manifest expires after four hours for Apply; regenerate audit
evidence and a new session when needed, not the large backup.

The existing CLI performs reversible LOGICAL retirement, not physical
deletion: no source originals, asset/revision rows, faces, actions or
review history are deleted. Preserve each batch's manifest and durable
receipt for selective rollback.

After **each approved batch**, run scoped `verify-retired` plus the
global `tools/archive-case-alias-retirement-postcheck.sql`, checking
the expected active mapping count and zero conflicts. At the end,
restart the SAME known-good app package (do not migrate the database
mid-session) and open the five precise surviving photographs listed
in the private review checklist for each batch. Run the inventory
again to confirm the active-casefold count declined.

## Still blocked

The originally held ~755 groups are a conservative count under the
old older-survivor-and-equal-annotations policy, not evidence that
all of them need manual review. The new lowercase-canonical preview
splits them into proof failures, explicit upper-history reconciliation
and otherwise safe candidates awaiting a revised transaction service.

Those groups can contain human/unknown reviews,
manual tags, places, people, capture dates, list collections, featured
face references, ambiguous order, absent hash proof or differing
effective annotations. A later separate change must reclassify them
by reason and explicitly reconcile unique history **before** deciding
to hide the later asset. Do not use scripts that source-exclude/purge,
or SQL DELETE, as a shortcut.
