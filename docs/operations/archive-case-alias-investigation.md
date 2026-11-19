# Archive case-only aliases: prevention and review

## Finding (2026-11-19)

After archive coverage was removed and re-added, the maintainer identified
825 pairs of source records that compare equal when path separators and casing
are normalized. Both members of each pair were marked present; each has a
different asset and revision ID. In these pairs, directory capitalization
differs (for example, fideli/ versus Fideli/), while filenames match.

**Correction to the early investigation:** the initial PowerShell diagnostic
wrapped the array returned by Invoke-RestMethod as one element. That incorrectly
assigned GroupNumber=1 to all 1,872 copies. The follow-up filesystem hashing
then compared files belonging to different groups; it did not demonstrate an
incorrect stored SHA-256. A PostgreSQL query found two members per top hash
group with identical sizes. Content-hash corruption is not established.

Unpack the duplicate API's group array explicitly in PowerShell 7:

~~~powershell
$response = Invoke-RestMethod 'http://127.0.0.1:5080/api/archive/exact-duplicates'
$groups = @(foreach ($group in $response) { $group })
[pscustomobject]@{
    Groups = $groups.Count
    Copies = @($groups | ForEach-Object { $_.copies }).Count
}
~~~

Hash equality between different archive-relative paths is not a reason to
discard either path. The immediate issue is case-only path aliases.

## Preventative safety change

The PostgreSQL batch scanner now refuses an unseen source key if an existing
asset under the same source ID has that key with different capitalization.
This is a fail-closed guard, **not an automatic merge or repair**. It protects
annotations on the existing record and prevents further case-only aliases
from being inserted. Existing pairs still require operator reconciliation.

The exclusion lookup now recognizes incoming case-only spellings of durable
source exclusions before the source is hashed or opened. A persistence-level
check provides a second fence. No source originals or database annotations
are deleted by this change.

## Read-only production audit

Take and verify a production backup before any future repair. The audit SQL
uses a read-only PostgreSQL transaction and does not open OneDrive files.

From the repository root in PowerShell 7.6:

~~~powershell
New-Item -ItemType Directory -Path '.\artifacts' -Force | Out-Null
psql -X -q -v ON_ERROR_STOP=1 -d photoidentity_main -f '.\tools\archive-case-alias-audit.sql' > '.\artifacts\archive-case-alias-audit.csv'

$review = @(Import-Csv '.\artifacts\archive-case-alias-audit.csv')
$collisionGroups = @(
    $review | Group-Object -Property { "$($_.source_id)|$($_.normalized_source_key)" }
)
$collisionGroups | ForEach-Object { $_.Group[0] } | Group-Object review_classification |
    Select-Object Name, Count | Format-Table
~~~

The output CSV contains **private** paths, identifiers, and manual history
counts. Do not commit it or attach it to a public PR.

Each row reports asset creation and missing state, current verified revision
and hash (if any), plus history counts across all revisions: photo tags,
manually associated people, manually set Places, face-review actions and
person labels. Face occurrence totals are also included. Counts represent
history, not necessarily current effective assignments.

Review classifications:

- review-single-annotated-candidate: inspect the sole annotated asset as a
  strong keeper candidate; **no automatic purge**.
- manual-review-both-annotated: reconcile unique manual history before any
  destructive operation.
- manual-review-no-annotation-preference: creation time alone is not enough
  to select a keeper.
- manual-review-multiple-aliases: more than two aliases require manual review.

## Follow-up needed before any production cleanup

A later, separately reviewed change must propose a **dry-run-only** merge
plan, identify a keeper without losing annotations, preserve existing asset
and revision identities, handle both-annotated conflicts, reconcile source
observations and missing-state behavior across coverage changes, and verify
the result against the permanent archive. No automatic keeper selection,
migration, exclusion, purge, or data repair is part of this safety slice.

Do not use resolve-exact-duplicates.ps1,
exclude-exact-duplicates-by-prefix.ps1, or lifecycle Exclude & purge to
repair these 825 case-only pairs.

## Verification

The PostgreSQL regression test requires the disposable-test database
environment variable PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING.

~~~powershell
dotnet test .\tests\PhotoIdentity.Persistence.Tests\PhotoIdentity.Persistence.Tests.csproj --filter FullyQualifiedName~PostgresArchiveCaseAliasGuardTests
~~~

The test checks exact-path preservation, prevention of a new case-only alias,
retention of manual tag/person history and recognition of case-only
aliases of durable exclusion tombstones. Live PostgreSQL verification and
the private production audit are still outstanding.


## Controlled automated retirement: first eligibility gate

The private effective-state audit reported 1,088 case-only pairs with matching
verified hashes and no incomplete current verification. It also found 762
pairs with effective differences (373 manual people; 735 Place/provenance;
27 reviewed-face-count differences; overlapping categories). **These are
not 1,088 safe deletions.**

The application's PostgresIdentityAutoAssignmentService writes decisions
with the exact actors identity-matcher:auto and
identity-matcher:auto-multi-evidence. That provides evidence for classifying
automatically generated actions separately from human or unknown actors.
An automatic action does not justify deleting a different path or revision.

New script tools/archive-case-alias-retirement-evidence.sql examines both
assets in each case-only pair and counts human/unknown versus automatic face
reviews and person labels, manually applied tags, manually linked people,
manual Place edits, capture-date overrides, presentation preferences, manual
list-collection references, and featured-person face references across
**all asset revisions**. It also reports verified file size, current hash,
and durable source-exclusion matches. It is a PostgreSQL READ ONLY audit.

The offline PowerShell planner uses that evidence and the existing private
effective-state comparison. A pair can be marked for a proposed **reversible
logical retirement** only if all the following hold:

1. Exactly two different assets share the same normalized case-insensitive
   path under the same source; their exact stored key spellings are distinct.
2. Both current observations are verified, with identical valid SHA-256
   digests AND byte sizes; neither asset is marked missing or excluded.
3. The proposed survivor has a **strictly earlier creation timestamp**.
   A tie is blocked rather than guessing from GUID sorting.
4. The later asset has **zero** human/unknown face-review actions and
   labels; zero manual tag/person/Place actions, capture-date or
   presentation preference actions; and no manual collection items
   or featured-person face references.
5. Both current revisions agree on the effective tags, manual people,
   Place value and provenance, and confirmed face-person IDs.
   Differences in automatic face-review counts are not independently
   treated as human edits, but their data is not deleted either.

Unrecognized actor values are always classified human/unknown and block
the proposal. A proposed survivor is never selected from SHA-256 alone.
The earlier asset is preferred **only within the same case-normalized path**.

An important safety boundary: this outputs an **unapproved CANDIDATE
MANIFEST**, not an executable cleanup operation. ApplyEnabled is False
for every row, AssetRowsToDelete=0, and OriginalFilesToDelete=0.
The manifest is deliberately a review stage for a future reversible
database-backed alias retirement, not a SQL DELETE list.

### Run the eligibility gate on the Windows host

After checking out the branch, retain the previous private
artifacts/archive-case-effective-state.csv file, and reuse the same
validated local Podman $containerId as before.

~~~powershell
pwsh -NoProfile -File .\tests\PowerShell\archive-case-alias-retirement-plan.tests.ps1

$sql = Get-Content '.\tools\archive-case-alias-retirement-evidence.sql' -Raw
$result = $sql | & podman exec -i $containerId sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -h 127.0.0.1 -U "$POSTGRES_USER" -d photoidentity_main -X -q -v ON_ERROR_STOP=1'
if ($LASTEXITCODE -ne 0) { throw 'Read-only retirement evidence audit failed.' }
$result | Set-Content '.\artifacts\archive-case-retirement-evidence.csv' -Encoding utf8

.\tools\new-archive-case-alias-retirement-plan.ps1 -EvidenceCsv '.\artifacts\archive-case-retirement-evidence.csv' -EffectiveStateCsv '.\artifacts\archive-case-effective-state.csv' -OutputDirectory '.\artifacts\archive-case-retirement'
~~~

Share only the aggregate summary, never the private CSV files. They contain
source paths, original asset IDs and hash values.

### What the later apply stage must implement

Before any production writes, develop a **reversible logical retirement**
that keeps the secondary asset, its revision identities, all face
observations, automatic suggestions, human review history and metadata
in PostgreSQL. Use an explicit mapping from a retired alias to the
surviving asset; do not use source-copy exclusion or purging.

Integrate this mapping with scan/verification (or a later scan will recreate
or reactivate the alias), the library search, Smart Collections, exact
duplicates, face review, and other active-photo views. Require an isolated
restore test, transactional preconditions, idempotency, operator approval
for a bounded batch, audit logging, rollback, and post-repair checks.
Conflicting annotations must be addressed separately rather than silently
overwritten or discarded.

This is a preferred staging direction; no production database cleanup is
authorized by the first eligibility gate.


## Reversible-retirement storage and rescan integration (development rehearsal)

A subsequent, separately reviewed development slice introduces a **dormant**
v43 schema, archive_case_alias_retirements. It stores the selected survivor
asset ID, secondary asset ID, exact source locators, common content hash,
retirement timestamp, actor, and nullable restore timestamp/actor.

**No production data is retired by applying the schema.** There is no API,
CLI or operator-facing apply/restore command in this slice. A disposable
PostgreSQL integration test creates a synthetic pair, soft-marks the later
asset as missing and inserts a mapping in one controlled fixture. The
existing asset, revision, tag and face review IDs are never deleted.

With an active mapping, the PostgreSQL scan batch maps the second asset's
source-key spelling to the original asset ID *before* the batch upsert. It
fails closed if the source mapping or deletion state is inconsistent, and
uses the original source path only for opening the physical original.
Missing reconciliation recognizes the observed secondary spelling as an
observation of the survivor. This avoids reactivating the secondary asset
or marking the survivor missing on the next rescan. Exact-duplicate inventory
omits active retired aliases but continues to report genuine distinct-path
copies even when their SHA-256 matches.

After a mapping's restore timestamp is populated and the secondary asset
is made present, its exact source key is processed independently again.
The disposable test checks this rollback behavior and verifies the original
annotation and automatic face review history remain intact.

### Verification (disposable database only)

~~~powershell
.\verify-postgres.ps1 -SkipContainerStart
~~~

Or, with PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING configured to a
disposable PostgreSQL service:

~~~powershell
dotnet test .\tests\PhotoIdentity.Persistence.Tests\PhotoIdentity.Persistence.Tests.csproj --filter FullyQualifiedName~PostgresArchiveCaseAliasRetirementMappingTests
~~~

**Not yet a production apply mechanism.** Before enabling any automated
retirement, implement a transactionally guarded command which revalidates
candidate evidence *inside the write transaction*, creates the mapping,
soft-retires the secondary row, logs every action, supports idempotent
replay and rollback, and refuses any missing/ambiguous evidence.

The active face review queues and all photo library/search/collection
queries must also be verified to ignore retired aliases; not all existing
query surfaces use the same active-asset filter. Rollback and rescan must be
tested end-to-end with production-like synthetic face/Place metadata. Until
that work is complete, do not manually insert mapping rows or set asset
deleted_at_utc in photoidentity_main. The production backup must be
restore-tested before the first approved apply.
