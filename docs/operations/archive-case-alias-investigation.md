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


## Follow-up audit result and read-only reconciliation preview

The production read-only audit returned 2,176 asset records grouped into 1,088
case-normalized pairs:

| Annotation history per pair | Pairs |
| --- | ---: |
| Both members have captured manual history | 458 |
| Exactly one has captured manual history | 409 |
| Neither has captured manual history | 221 |

This audit is broader than the earlier exact-duplicate view (825 pairs): the
263 additional pairs may have unverified, missing, excluded, or differing
hash evidence. They must not be treated as exact byte duplicates.

A separately reviewed, read-only preview consumes the private CSV generated
by the audit; it does not connect to PostgreSQL. PostgreSQL COPY CSV uses
`t` and `f` for Boolean values (including `marked_missing`); the
preview accepts those values as well as `True` and `False` from synthetic
PowerShell CSV fixtures:

~~~powershell
pwsh -NoProfile -File .\tests\PowerShell\archive-case-reconciliation-preview.tests.ps1
.\tools\preview-archive-case-reconciliation.ps1 -AuditCsv '.\artifacts\archive-case-alias-audit.csv' -OutputDirectory '.\artifacts\archive-case-reconciliation'
~~~

It writes a private CSV with one row per case-collision group and prints a
privacy-safe aggregate summary. The preview calculates:

- whether both current source revisions are verified and report matching,
  different, or incomplete SHA-256 evidence;
- whether a member is marked missing;
- the number of members with manual annotation history;
- a non-binding keeper candidate ID only if exactly one member has captured
  manual annotation history;
- a conservative decision for manual review.

Every row has SafeToDelete=false and RequiresOperatorReview=true.
The script has no Apply mode. Same-hash evidence is not interpreted as a
license to delete an archive-relative source locator. Face occurrences and
other processing-derived metadata may exist on either asset even when the
captured manual action counts are zero.

A later design must inspect effective annotation values, revision-linked
processing history, identity assignments and derived references before
creating any destructive reconciliation transaction. Existing source IDs,
asset IDs and revision IDs must remain stable for the authoritative records.
Case-only paths may be aliases of the same Windows file, but the database's
present flag does not prove current filesystem existence.

The audit and preview CSV files are private and belong in the ignored
artifacts/ directory. Never attach them to public PRs.
