# Real Podman/PostgreSQL boundary test for WI-0208. Only creates a fresh,
# randomly named disposable Compose project/volume and synthetic catalogue.
# Must be run by an operator with -ConfirmDisposableService; never from CI.
[CmdletBinding()]
param([switch]$ConfirmDisposableService)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $ConfirmDisposableService) {
    throw 'Explicit -ConfirmDisposableService is required. This test creates and removes a randomly named disposable Podman Compose volume.'
}

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$composeDir = Join-Path $root 'deploy\postgres'
$podman = Get-Command podman -ErrorAction Stop
$prefix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$project = "photoidentity_wi0208_test_$prefix"
$sourceDb = 'wi0208_synthetic_source'
$temp = Join-Path ([IO.Path]::GetTempPath()) ("photoidentity-wi0208-$prefix")
[IO.Directory]::CreateDirectory($temp) | Out-Null
$backupPath = Join-Path $temp 'synthetic.dump'
$oldVars = @{}
$vars = @{
    COMPOSE_PROJECT_NAME = $project
    PHOTOIDENTITY_POSTGRES_PORT = [string](55000 + (Get-Random -Minimum 0 -Maximum 8000))
    PHOTOIDENTITY_POSTGRES_DATABASE = $sourceDb
    PHOTOIDENTITY_POSTGRES_USER = 'wi0208_tester'
    PHOTOIDENTITY_POSTGRES_PASSWORD = [Guid]::NewGuid().ToString('N')
}
foreach ($name in $vars.Keys) {
    $oldVars[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $vars[$name], 'Process')
}
$attempted = $false

function Invoke-SyntheticSql {
    param([string]$Container, [string]$Sql)
    & $podman.Source exec -e "FIXTURE_SQL=$Sql" $Container sh -lc 'psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -c "$FIXTURE_SQL"' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Disposable synthetic SQL failed.' }
}
function Assert-Test {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Disposable PostgreSQL test failed: $Message" }
}
function Get-TestReport {
    return (Get-Content -LiteralPath "$backupPath.restore-verification.json" -Raw | ConvertFrom-Json)
}

try {
    Push-Location $composeDir
    try {
        $attempted = $true
        & $podman.Source compose -p $project up -d postgres | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Disposable PostgreSQL Compose service failed to start.' }
        $container = (& $podman.Source compose -p $project ps -q postgres).Trim()
        if ([string]::IsNullOrWhiteSpace($container)) { throw 'Disposable PostgreSQL container is missing.' }
        $ready = $false
        for ($i = 0; $i -lt 30; $i++) {
            & $podman.Source exec $container sh -lc 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"' *> $null
            if ($LASTEXITCODE -eq 0) { $ready = $true; break }
            Start-Sleep -Seconds 2
        }
        if (-not $ready) { throw 'Disposable PostgreSQL never became ready.' }

        # Only synthetic, non-personal fixture tables. The extra schema
        # markers satisfy the normal backup wrapper's catalogue checks.
        $sql = 'CREATE TABLE public.photo_identity_schema_migrations(version int NOT NULL); INSERT INTO public.photo_identity_schema_migrations VALUES (1); CREATE TABLE public.asset_revisions(id int); CREATE TABLE public.review_actions(id int); INSERT INTO public.review_actions VALUES (1);'
        Invoke-SyntheticSql -Container $container -Sql $sql
    }
    finally {
        Pop-Location
    }

    & (Join-Path $root 'backup-postgres-catalogue.ps1') -DatabaseName $sourceDb -OutputPath $backupPath
    Assert-Test (Test-Path -LiteralPath $backupPath -PathType Leaf) 'synthetic backup was not created'

    & (Join-Path $root 'verify-postgres-backup-restore.ps1') -BackupPath $backupPath -ApplicationStopped
    $report = Get-TestReport
    Assert-Test ($report.passed -and $report.cleanupStatus -eq 'removed' -and -not $report.cleanupRequired) 'successful restore not removed'
    $inventory = @(& (Join-Path $root 'manage-postgres-restores.ps1'))
    Assert-Test (@($inventory | Where-Object Name -eq $report.verificationDatabase).Count -eq 0) 'successful restore still exists'
    Assert-Test (Test-Path -LiteralPath "$backupPath.json" -PathType Leaf) 'backup hash report disappeared'
    Write-Host 'PASS: successful isolated restore removed, backup preserved.'

    & (Join-Path $root 'verify-postgres-backup-restore.ps1') -BackupPath $backupPath -ApplicationStopped -RetainRestoredDatabase
    $report = Get-TestReport
    Assert-Test ($report.passed -and $report.cleanupStatus -eq 'retained') 'explicit retain failed'
    $retainedName = $report.verificationDatabase

    $duplicateRejected = $false
    try { & (Join-Path $root 'verify-postgres-backup-restore.ps1') -BackupPath $backupPath -ApplicationStopped }
    catch { $duplicateRejected = $true }
    Assert-Test $duplicateRejected 'same-backup unsafe retry was not refused'

    & (Join-Path $root 'verify-postgres-backup-restore.ps1') -BackupPath $backupPath -ApplicationStopped -VerificationDatabaseName $retainedName -ResumeRestoredDatabase
    $report = Get-TestReport
    Assert-Test ($report.passed -and $report.cleanupStatus -eq 'retained') 'read-only resume removed its copy'

    & (Join-Path $root 'manage-postgres-restores.ps1') -RemoveDatabase $report.verificationDatabase -ExpectedMarker $report.restoreOwnershipMarker -ProductionDatabaseName $sourceDb -ConfirmRemoval
    $inventory = @(& (Join-Path $root 'manage-postgres-restores.ps1'))
    Assert-Test (@($inventory | Where-Object Name -eq $retainedName).Count -eq 0) 'confirmed removal failed'
    Write-Host 'PASS: retain/resume/duplicate protection/confirmed cleanup.'

    # The source is deliberately changed after the backup. Restoration
    # succeeds but exact comparison must fail and preserve an owned copy.
    Push-Location $composeDir
    try {
        Invoke-SyntheticSql -Container $container -Sql 'INSERT INTO public.review_actions VALUES (2);'
    }
    finally {
        Pop-Location
    }
    $comparisonRejected = $false
    try { & (Join-Path $root 'verify-postgres-backup-restore.ps1') -BackupPath $backupPath -ApplicationStopped }
    catch { $comparisonRejected = $true }
    Assert-Test $comparisonRejected 'deliberate comparison mismatch did not fail'
    $report = Get-TestReport
    Assert-Test (-not $report.passed -and $report.cleanupStatus -eq 'retained-failure') 'failed comparison was not retained'
    $inventory = @(& (Join-Path $root 'manage-postgres-restores.ps1'))
    Assert-Test (@($inventory | Where-Object Name -eq $report.verificationDatabase).Count -eq 1) 'failed copy missing from inventory'
    & (Join-Path $root 'manage-postgres-restores.ps1') -RemoveDatabase $report.verificationDatabase -ExpectedMarker $report.restoreOwnershipMarker -ProductionDatabaseName $sourceDb -ConfirmRemoval
    Write-Host 'PASS: comparison failure retained for diagnosis and exact-name cleanup.'
}
finally {
    if ($attempted -and $project -cmatch '^photoidentity_wi0208_test_[a-f0-9]{12}$') {
        # Absolutely scoped to the new random disposable Compose project.
        Push-Location $composeDir
        try {
            & $podman.Source compose -p $project down -v 2>$null | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Disposable Compose cleanup failed; project $project requires manual inspection. NEVER remove the production Compose volume."
            }
        }
        finally { Pop-Location }
    }
    foreach ($name in $vars.Keys) {
        [Environment]::SetEnvironmentVariable($name, $oldVars[$name], 'Process')
    }
    Remove-Item -LiteralPath $temp -Force -Recurse -ErrorAction SilentlyContinue
}
