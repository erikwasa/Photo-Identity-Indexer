# Source-free process-boundary contract tests: in-process fake Podman + real verifier.
# No personal data, service, PostgreSQL cluster or production files are touched.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$verifier = Join-Path $root 'verify-postgres-backup-restore.ps1'
$manager = Join-Path $root 'manage-postgres-restores.ps1'
$global:MockPodman = Join-Path $PSScriptRoot 'fake-podman-restore.ps1'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('wi0208-endtoend-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp) | Out-Null
$backupPath = Join-Path $temp 'synthetic.dump'
$reportPath = "$backupPath.restore-verification.json"
$backupManifest = "$backupPath.json"
$script:cases = 0

# Override command discovery only for podman. The returned .ps1 is executed
# in-process, making command counts and failures deterministic.
function Get-Command {
    [CmdletBinding()]
    param([Parameter(Position = 0, Mandatory = $true)][string]$Name)
    if ($Name -eq 'podman') {
        return [pscustomobject]@{ Source = $global:MockPodman }
    }
    return (Microsoft.PowerShell.Core\Get-Command -Name $Name)
}
function Assert-True {
    param([bool]$Condition, [string]$Explanation)
    if (-not $Condition) { throw "ASSERTION FAILED: $Explanation" }
}
function Reset-Scenario {
    $global:FakeRestore = [ordered]@{
        Name = ''
        Exists = $false
        Marker = ''
        Sessions = 0
        TransientSessionsRemaining = 0
        LastObservedSessionCount = 0
        Created = 0
        Copies = 0
        Restores = 0
        DropAttempts = 0
        TempCleans = 0
        RestoreFails = $false
        Interrupts = $false
        DropFails = $false
        CompareMismatch = $false
    }
    Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue
}
function Verify-Failed {
    param([scriptblock]$Action, [string]$Explanation)
    $failed = $false
    try { & $Action } catch { $failed = $true }
    Assert-True $failed $Explanation
}
function Check-Report {
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw 'Restore verification report was not written.'
    }
    return (Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json)
}
function Passed-Case {
    param([string]$Name)
    $script:cases++
    Write-Host "PASS: $Name"
}

try {
    [IO.File]::WriteAllBytes($backupPath, [byte[]](1, 2, 3, 4, 5, 6))
    $hash = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        sourceDatabase = 'photoidentity_main'
        backupSha256 = $hash
        format = 'pg_dump-custom'
    } | ConvertTo-Json | Set-Content -LiteralPath $backupManifest -Encoding UTF8

    Reset-Scenario
    & $verifier -BackupPath $backupPath -ApplicationStopped
    $r = Check-Report
    Assert-True ($r.passed -and $r.cleanupStatus -eq 'removed' -and -not $r.cleanupRequired) 'success must report completed cleanup'
    Assert-True (-not $global:FakeRestore.Exists -and $global:FakeRestore.DropAttempts -eq 1) 'successful fresh verification should remove only owned copy'
    Assert-True ((Test-Path $backupPath) -and (Test-Path $backupManifest)) 'backup and hash evidence preserved'
    Assert-True ($global:FakeRestore.TempCleans -eq 1) 'container copy removed after success'
    Passed-Case 'fresh success and cleanup'

    Reset-Scenario
    # First post-restore details check and first cleanup check see an
    # ephemeral PostgreSQL session; the subsequent check is idle.
    $global:FakeRestore.TransientSessionsRemaining = 2
    & $verifier -BackupPath $backupPath -ApplicationStopped
    $r = Check-Report
    Assert-True ($r.passed -and $r.cleanupStatus -eq 'removed' -and -not $r.cleanupRequired) 'transient session cleanup must be reported as removed'
    Assert-True (-not $global:FakeRestore.Exists -and $global:FakeRestore.DropAttempts -eq 1) 'transient sessions must not strand a successfully verified restore'
    Assert-True ($global:FakeRestore.TransientSessionsRemaining -eq 0) 'transient session scenario was exercised'
    $samples = @($r.cleanupBusyDiagnostics)
    Assert-True ($samples.Count -eq 1 -and $samples[0].attempt -eq 1) 'busy session captured in this restore run'
    Assert-True ($samples[0].sessionsAtOwnershipCheck -eq 1 -and
        $samples[0].snapshot.activity[0].backendType -eq 'autovacuum worker' -and
        $samples[0].snapshot.activity[0].clientKind -eq 'internal') 'classified background worker without identity'
    Assert-True ($samples[0].snapshot.analyzeRunning -eq 1 -and
        $samples[0].snapshot.vacuumRunning -eq 0) 'maintenance-progress counts captured'
    Passed-Case 'post-restore transient session clears within bounded wait'

    Reset-Scenario
    & $verifier -BackupPath $backupPath -ApplicationStopped -RetainRestoredDatabase
    $r = Check-Report
    Assert-True ($r.passed -and $r.cleanupStatus -eq 'retained' -and $r.cleanupRequired) 'inspection mode retained'
    Assert-True ($global:FakeRestore.Exists -and $global:FakeRestore.DropAttempts -eq 0) 'explicit retention never drops'
    $originalName = $r.verificationDatabase
    Verify-Failed { & $verifier -BackupPath $backupPath -ApplicationStopped } 'fresh retry must not create a second copy for the same backup'
    Assert-True ($global:FakeRestore.Created -eq 1 -and $global:FakeRestore.DropAttempts -eq 0) 'duplicate retry was prevented before create'
    $created = $global:FakeRestore.Created
    $copies = $global:FakeRestore.Copies
    $restores = $global:FakeRestore.Restores
    & $verifier -BackupPath $backupPath -ApplicationStopped -VerificationDatabaseName $originalName -ResumeRestoredDatabase
    $r = Check-Report
    Assert-True ($r.passed -and $r.cleanupStatus -eq 'retained') 'resume is read-only and keeps copy'
    Assert-True ($global:FakeRestore.Created -eq $created -and $global:FakeRestore.Copies -eq $copies -and $global:FakeRestore.Restores -eq $restores) 'resume did not create or restore'
    $inventory = @(& $manager)
    Assert-True ($inventory.Count -eq 1 -and $inventory[0].Ownership -eq 'owned') 'read-only inventory identifies owned restore'
    Assert-True ($global:FakeRestore.DropAttempts -eq 0) 'inventory never drops'
    & $manager -RemoveDatabase $originalName -ExpectedMarker $r.restoreOwnershipMarker -ProductionDatabaseName photoidentity_main -ConfirmRemoval
    Assert-True (-not $global:FakeRestore.Exists -and $global:FakeRestore.DropAttempts -eq 1) 'confirmed, exactly-owned cleanup'
    Passed-Case 'retain, resume, inventory, exact-name manual disposal'

    Reset-Scenario
    $global:FakeRestore.RestoreFails = $true
    Verify-Failed { & $verifier -BackupPath $backupPath -ApplicationStopped } 'restore failure must fail'
    $r = Check-Report
    Assert-True (-not $r.passed -and $r.cleanupStatus -eq 'retained-failure') 'restore failure reported and retained'
    Assert-True ($global:FakeRestore.Exists -and $global:FakeRestore.DropAttempts -eq 0) 'failed restore not dropped'
    Assert-True ($global:FakeRestore.TempCleans -eq 1) 'container copy cleaned after failed restore'
    Passed-Case 'restore failure preservation'

    Reset-Scenario
    $global:FakeRestore.Interrupts = $true
    Verify-Failed { & $verifier -BackupPath $backupPath -ApplicationStopped } 'interrupting restore must fail'
    $r = Check-Report
    Assert-True (-not $r.passed -and $r.cleanupStatus -eq 'retained-failure') 'interruption report retains failure evidence'
    Assert-True ($global:FakeRestore.Exists -and $global:FakeRestore.DropAttempts -eq 0 -and $global:FakeRestore.TempCleans -eq 1) 'interrupted restore preserved with temporary copy removed'
    Passed-Case 'interrupted restore preservation'

    Reset-Scenario
    $global:FakeRestore.CompareMismatch = $true
    Verify-Failed { & $verifier -BackupPath $backupPath -ApplicationStopped } 'comparison failure must fail'
    $r = Check-Report
    Assert-True (-not $r.passed -and $r.cleanupRequired) 'comparison failure diagnostic report'
    Assert-True ($global:FakeRestore.Exists -and $global:FakeRestore.DropAttempts -eq 0) 'comparison failure copy retained'
    Passed-Case 'comparison failure preservation'

    Reset-Scenario
    $global:FakeRestore.DropFails = $true
    Verify-Failed { & $verifier -BackupPath $backupPath -ApplicationStopped } 'cleanup failure must fail command'
    $r = Check-Report
    Assert-True ($r.passed -and $r.cleanupStatus -eq 'failed' -and $r.cleanupRequired) 'verified restore is distinct from completed cleanup'
    Assert-True ($global:FakeRestore.Exists -and $global:FakeRestore.DropAttempts -eq 1) 'failed cleanup is not silently ignored'
    Assert-True (@($r.cleanupSessionSummary).Count -eq 1) 'cleanup failure records bounded backend diagnostics'
    Assert-True ($r.cleanupSessionSummary[0].backendType -eq 'client backend' -and
        $r.cleanupSessionSummary[0].sessions -eq 1) 'late client session is reported by type/count only'
    Assert-True ($r.cleanupSessionSummary[0].PSObject.Properties.Name -notcontains 'pid' -and
        $r.cleanupSessionSummary[0].PSObject.Properties.Name -notcontains 'query' -and
        $r.cleanupSessionSummary[0].PSObject.Properties.Name -notcontains 'applicationName') 'no identities or queries in diagnostic report'
    Passed-Case 'cleanup failure surfaced with sanitized session diagnostics'

    Reset-Scenario
    [IO.Directory]::CreateDirectory($reportPath) | Out-Null
    try {
        Verify-Failed { & $verifier -BackupPath $backupPath -ApplicationStopped } 'report-write failure must fail'
        Assert-True ($global:FakeRestore.Exists -and $global:FakeRestore.DropAttempts -eq 0) 'nothing dropped before durable report'
    }
    finally {
        Remove-Item -LiteralPath $reportPath -Recurse -Force
    }
    Passed-Case 'report publication before cleanup'

    Reset-Scenario
    Verify-Failed { & $verifier -BackupPath $backupPath -ApplicationStopped -VerificationDatabaseName photoidentity_main } 'production name prohibited'
    Assert-True ($global:FakeRestore.Created -eq 0 -and $global:FakeRestore.DropAttempts -eq 0) 'production name never created or dropped'
    Passed-Case 'production name protection'

    Reset-Scenario
    & $verifier -BackupPath $backupPath -ApplicationStopped -RetainRestoredDatabase
    $r = Check-Report
    $global:FakeRestore.Marker = ''
    Verify-Failed { & $manager -RemoveDatabase $r.verificationDatabase -ExpectedMarker $r.restoreOwnershipMarker -ProductionDatabaseName photoidentity_main -ConfirmRemoval } 'legacy/unmarked cleanup prohibited'
    Assert-True ($global:FakeRestore.DropAttempts -eq 0) 'unowned database not dropped'
    Passed-Case 'legacy ownership refusal'

    Write-Host "WI-0208 full synthetic process-boundary scenarios: $script:cases passed."
}
finally {
    Remove-Item -LiteralPath $temp -Force -Recurse -ErrorAction SilentlyContinue
}
