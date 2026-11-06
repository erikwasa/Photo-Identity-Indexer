# WI-0207 — Restart-safe managed video ownership (checkpoint 4)

**Checkpoint 3 accepted, 2026-11-06:** The maintained Windows
host passed 30 synthetic integration tests, one actual disposable
PostgreSQL lease test, and documentation validation and generation.
PR #752 merged the acceptance record. No repeat is required.

## Scope of this checkpoint

The video worker now restores an existing durable managed hydration
receipt if a previously app-hydrated original is already local after
a pause, restart or retry. It touches usage and respects a pending
release. Ordinary user-local originals without active ownership are
never claimed or automatically released by this recovery path.

On an operator-started video Archive advance, a bounded PostgreSQL
query returns at most 16 existing release-requested video receipts.
Only when Windows metadata already reports an original online-only
and unpinned does the recovery service **conditionally** mark the
receipt released. The PostgreSQL update requires the release request
to remain outstanding; it never closes a newer or unrelated receipt.
All local, pinned, downloading, missing, linked, unknown or error
states retain ownership. The recovery pass never issues a new
OneDrive download, pin, unpin, eviction or source-byte read.

**Keep Include online-only videos OFF.** This checkpoint is
synthetic/disposable-PostgreSQL-only, not production release approval.

## Step 1 — Update main (PowerShell 7.6)

~~~powershell
$ErrorActionPreference = "Stop"
cd "C:\Kod\codex\Photo Identity Indexer"
git status --short
git switch main
if ($LASTEXITCODE -ne 0) { throw "Cannot switch main." }
git pull --ff-only origin main
if ($LASTEXITCODE -ne 0) { throw "Cannot fast-forward main." }
~~~

Do not discard any uncommitted work if Git refuses the update.

## Step 2 — Run synthetic, no-media tests

~~~powershell
$filter = @(
    "FullyQualifiedName~VideoManagedHydrationRecoveryTests"
    "FullyQualifiedName~VideoManagedHydrationAdmissionTests"
    "FullyQualifiedName~VideoHydrationReleaseGuardTests"
) -join "|"

dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter $filter
if ($LASTEXITCODE -ne 0) { throw "WI-0207 restart tests failed." }
Write-Host "WI-0207 checkpoint 4 synthetic tests: PASS"
~~~

The tests use fake OneDrive states and durable receipt fakes
with empty temporary directories. They do not open an original,
process a video or modify OneDrive pinning.

## Step 3 — Verify bounded SQL in disposable PostgreSQL

Your previous session had the test-only PostgreSQL administrator
variable configured. The test creates and drops a uniquely named
temporary database, not photoidentity_main. Do not substitute
production credentials and do not print credential values.

~~~powershell
$testAdmin = [Environment]::GetEnvironmentVariable(
    "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING", "Process"
)
$postgresExecuted = -not [string]::IsNullOrWhiteSpace($testAdmin)
Remove-Variable testAdmin -ErrorAction SilentlyContinue
Write-Host "Disposable PostgreSQL configured: $postgresExecuted"

if ($postgresExecuted) {
    dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj -c Release --filter "FullyQualifiedName~PostgresVideoManagedHydrationTests"
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL recovery test failed." }
    Write-Host "WI-0207 disposable PostgreSQL test: PASS"
}
if (-not $postgresExecuted) {
    Write-Host "WI-0207 disposable PostgreSQL test: NOT EXECUTED"
}
~~~

The test verifies durable video/photo receipts, that only requested
video releases are returned with a database-side limit, and that
confirmed releases disappear from the pending query. A green test
without the variable does not prove database verification.

## Step 4 — Documentation checks

~~~powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw "Documentation validation failed." }
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
if ($LASTEXITCODE -ne 0) { throw "Generated documentation check failed." }
Write-Host "WI-0207 checkpoint 4 documentation: PASS"
~~~

Send only the test totals, whether the disposable PostgreSQL test
actually executed, and PASS/FAIL results. Do not run Archive,
change pin state or download a real online-only video for this PR.

## Remaining WI-0207 gates

The current +p app hydration pin remains indistinguishable from a
user choosing Always keep on this device. Pinned originals must
remain local. This checkpoint confirms crash receipt reconciliation,
but does not authorize automatic release of app-pinned originals
or prove race-free coordination with newly queued dependent jobs.
Those and one separately authorized real online-only hydrate,
process, reuse and release trial remain open.
