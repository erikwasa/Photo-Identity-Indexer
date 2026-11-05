# WI-0207 — Shared photo/video managed-hydration admission (checkpoint 3)

**Checkpoint 2 accepted 2026-11-05:** PR #749 merged; the
maintained Windows local tests passed 27/27, failed 0, skipped 0.

**Checkpoint 3 is synthetic/disposable-DB only.** The video Archive
worker reuses the photo managed-hydration capacity gate and records
ownership in the existing PostgreSQL managed-hydration table before
requesting a OneDrive pin. Photo and video managed receipts share the
same configured storage budget. Because a OneDrive app pin cannot be
distinguished from a user pin, the photo cache cleaner now excludes
video-owned originals from automatic LRU release. The conservative
release guard remains in effect.

**This checkpoint does not authorize any real OneDrive downloads.**
Keep Settings / Video processing / Include online-only videos OFF.

## 1. Update and test from PowerShell 7.6

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
if ($LASTEXITCODE -ne 0) { throw "Cannot switch main." }
git pull --ff-only origin main
if ($LASTEXITCODE -ne 0) { throw "Cannot update main." }

dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoManagedHydrationAdmissionTests|FullyQualifiedName~VideoHydrationReleaseGuardTests|FullyQualifiedName~VideoOnlineOnlyPreflightTests"
if ($LASTEXITCODE -ne 0) { throw "WI-0207 synthetic admission tests failed." }
$integrationPassed = $true
Write-Host "WI-0207 integration checks: PASS"
~~~

These tests use synthetic states and static safeguards and do not
open private original media or change Files On-Demand attributes.

## 2. Verify video/photo lease durability in disposable PostgreSQL

This test runs only if the existing environment variable
PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING is configured.
If absent, the test runner returns without actually testing database
persistence. Do not interpret a green test result as a verified DB
operation in that case. Never print credentials.

~~~powershell
$adminAvailable = -not [string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable(
        "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING", "Process"
    )
)
Write-Host "Disposable PostgreSQL admin configured: $adminAvailable"

if ($adminAvailable) {
    dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj -c Release --filter "FullyQualifiedName~PostgresVideoManagedHydrationTests"
    if ($LASTEXITCODE -ne 0) {
        throw "WI-0207 disposable PostgreSQL test failed."
    }
}
if (-not $adminAvailable) {
    Write-Host "Disposable PostgreSQL persistence test NOT EXECUTED."
}
~~~

This creates/drops a uniquely named temporary test database; it does
not alter photoidentity_main or open any video.

## 3. Validate docs

~~~powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw "Documentation validation failed." }
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
if ($LASTEXITCODE -ne 0) { throw "Generated docs are stale." }
Write-Host "WI-0207 checkpoint 3 docs: PASS"
~~~

## 4. Maintained Windows acceptance recorded — 2026-11-06

After PR #750 merged, the maintainer ran the selected tests and
reported **30 integration tests passed**, **1 real disposable
PostgreSQL ownership test passed** (admin environment variable
present), and both documentation validation and generated-doc
checks passed. Production video and OneDrive operations were not
performed. **Do not rerun these tests solely to correct the
summary's reporting variables.**

The session also demonstrated two benign PowerShell display
issues: entering an `else` clause as a later standalone
command does not attach it to the earlier `if` statement, and
an uninitialized `$integrationPassed` variable prints
`NOT VERIFIED` even after the test runner itself reported
30 passing tests. The commands above now assign that variable
when tests succeed and use a separate `if (-not ...)` in place
of a detachable `else` clause.

## 5. Remaining acceptance gates

This is not completion of WI-0207. The app's +p pin is indistinguishable
from a user selecting "Always keep on this device". Video receipts
therefore consume a shared bounded budget but are deliberately
retained when pinned, not silently unpinned. Cross-restart dependent
video jobs and a truly safe eventual release policy still need
their own reviewed implementation and synthetic tests. One real
operator-approved online-only video hydrate/process/reuse/release
trial is still mandatory after those safeguards.

Report only PASS/FAIL, totals, and whether the disposable PostgreSQL
test actually executed. No private filenames, frame images, source
paths or passwords are needed.
