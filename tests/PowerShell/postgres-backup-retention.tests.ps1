# Source-free WI-0209 backup-retention acceptance. Never uses real Podman,
# OneDrive, production dumps or an operator's approved backup directory.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tool = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'manage-postgres-backups.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('wi0209-backups-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$script:assertions = 0

function Check {
    param([bool]$Condition, [string]$Explanation)
    $script:assertions++
    if (-not $Condition) { throw "WI-0209 assertion failed: $Explanation" }
}
function Refuse {
    param([scriptblock]$Command, [string]$Explanation)
    $script:assertions++
    $threw = $false
    try { & $Command | Out-Null } catch { $threw = $true }
    if (-not $threw) { throw "WI-0209 unexpectedly allowed: $Explanation" }
}
function Make-Dump {
    param([string]$Name, [string]$Marker, [bool]$Verified = $true)
    $file = Join-Path $root $Name
    [IO.File]::WriteAllText($file, "synthetic backup $Marker, no catalogue or personal data")
    $sha = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $info = Get-Item -LiteralPath $file
    [ordered]@{
        backupPath = $file
        sourceDatabase = 'synthetic_photoidentity'
        backupSha256 = $sha
        backupBytes = [long]$info.Length
        format = 'pg_dump-custom'
    } | ConvertTo-Json | Set-Content -LiteralPath "$file.json" -Encoding utf8
    if ($Verified) {
        [ordered]@{
            passed = $true
            backupSha256 = $sha
            cleanupStatus = 'removed'
            cleanupRequired = $false
        } | ConvertTo-Json | Set-Content -LiteralPath "$file.restore-verification.json" -Encoding utf8
    }
    return [pscustomobject]@{ Path = $file; Sha = $sha }
}

try {
    $a = Make-Dump 'prechange-a.dump' 'alpha'
    $b = Make-Dump 'checkpoint-b.dump' 'beta'
    $c = Make-Dump 'checkpoint-c.dump' 'gamma'
    $noReport = Make-Dump 'no-restore.dump' 'delta' $false
    $unknown = Make-Dump 'unknown.dump' 'epsilon'

    $list = @(& $tool -BackupRoot $root)
    Check ($list.Count -eq 5) 'read-only inventory enumerates existing dumps'
    Check (@($list | Where-Object Role -ne 'unclassified').Count -eq 0) 'legacy/unknown files are not classified implicitly'
    Check ((Test-Path -LiteralPath $a.Path) -and -not (Test-Path -LiteralPath (Join-Path $root 'retention-state.json'))) 'inventory makes no writes'

    Refuse { & $tool -BackupRoot $root -PlanRemoval -BackupPath $unknown.Path -ExpectedSha256 $unknown.Sha } 'unclassified file does not get a deletion plan'
    Refuse { & $tool -BackupRoot $root -RemovePreChange -BackupPath $unknown.Path -ExpectedSha256 $unknown.Sha -ConfirmRemoval -ConfirmChangeAccepted -ChangeAcceptanceEvidence 'accepted' } 'unknown dump deletion'
    Refuse { & $tool -BackupRoot $root -RegisterPreChange -BackupPath $a.Path -ExpectedSha256 ('0' * 64) -ChangeReference 'migration-1' } 'mismatched backup fingerprint'
    Refuse { & $tool -BackupRoot $root -RegisterPreChange -BackupPath (Join-Path $root '..\escape.dump') -ExpectedSha256 $a.Sha -ChangeReference 'migration-1' } 'path traversal outside root'
    Refuse { & $tool -BackupRoot $root -RegisterPreChange -BackupPath $a.Path -ExpectedSha256 $a.Sha } 'missing change reference'
    Check (-not (Test-Path -LiteralPath (Join-Path $root 'retention-state.json'))) 'failed registration did not publish registry'

    $lockFile = Join-Path $root 'retention-state.lock'
    $heldLock = [IO.File]::Open($lockFile, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        Refuse { & $tool -BackupRoot $root -RegisterPreChange -BackupPath $a.Path -ExpectedSha256 $a.Sha -ChangeReference 'migration-1' } 'concurrent registry lock held'
        Check (-not (Test-Path -LiteralPath (Join-Path $root 'retention-state.json'))) 'concurrent registration makes no state write'
    }
    finally {
        $heldLock.Dispose()
    }

    & $tool -BackupRoot $root -RegisterPreChange -BackupPath $a.Path -ExpectedSha256 $a.Sha -ChangeReference 'migration-1'
    $list = @(& $tool -BackupRoot $root)
    Check (@($list | Where-Object { $_.Name -eq 'prechange-a.dump' -and $_.Role -eq 'pre-change' }).Count -eq 1) 'operator registration classified exact pre-change dump'
    $plan = & $tool -BackupRoot $root -PlanRemoval -BackupPath $a.Path -ExpectedSha256 $a.Sha
    Check ($plan.Role -eq 'pre-change' -and $plan.RemovalRequiresConfirmation) 'read-only exact-file prechange plan'
    Check (@($plan.Files).Count -eq 3 -and (Test-Path -LiteralPath $a.Path)) 'plan shows precisely three files and does not delete'
    Refuse { & $tool -BackupRoot $root -RegisterPreChange -BackupPath $a.Path -ExpectedSha256 $a.Sha -ChangeReference 'migration-2' } 'duplicate pre-change classification'
    Refuse { & $tool -BackupRoot $root -RemovePreChange -BackupPath $a.Path -ExpectedSha256 $a.Sha -ConfirmRemoval -ChangeAcceptanceEvidence 'accepted' } 'change acceptance switch missing'
    Refuse { & $tool -BackupRoot $root -RemovePreChange -BackupPath $a.Path -ExpectedSha256 $a.Sha -ConfirmChangeAccepted -ChangeAcceptanceEvidence 'accepted' } 'explicit removal confirmation missing'
    Refuse { & $tool -BackupRoot $root -RemovePreChange -BackupPath $a.Path -ExpectedSha256 $a.Sha -ConfirmChangeAccepted -ConfirmRemoval } 'post-change verification reference missing'
    Check ((Test-Path -LiteralPath $a.Path) -and (Test-Path -LiteralPath "$($a.Path).json")) 'refusal leaves pre-change bytes and manifest'

    Refuse { & $tool -BackupRoot $root -SetRecoveryCheckpoint -BackupPath $noReport.Path -ExpectedSha256 $noReport.Sha -ConfirmIndependentCopy -RemoteSha256 $noReport.Sha -RemoteConfirmation 'independently downloaded and checked' } 'non-restored checkpoint'
    Refuse { & $tool -BackupRoot $root -SetRecoveryCheckpoint -BackupPath $b.Path -ExpectedSha256 $b.Sha -RemoteSha256 $b.Sha -RemoteConfirmation 'web download verified' } 'remote explicit attestation flag missing'
    Refuse { & $tool -BackupRoot $root -SetRecoveryCheckpoint -BackupPath $b.Path -ExpectedSha256 $b.Sha -ConfirmIndependentCopy -RemoteSha256 ('f' * 64) -RemoteConfirmation 'web download verified' } 'remote fingerprint mismatch'
    Refuse { & $tool -BackupRoot $root -SetRecoveryCheckpoint -BackupPath $b.Path -ExpectedSha256 $b.Sha -ConfirmIndependentCopy -RemoteSha256 $b.Sha } 'remote availability evidence missing'

    & $tool -BackupRoot $root -SetRecoveryCheckpoint -BackupPath $b.Path -ExpectedSha256 $b.Sha -ConfirmIndependentCopy -RemoteSha256 $b.Sha -RemoteConfirmation 'web remote separately confirmed and downloaded hashed'
    $list = @(& $tool -BackupRoot $root)
    Check (@($list | Where-Object { $_.Name -eq 'checkpoint-b.dump' -and $_.Role -eq 'recovery-checkpoint' }).Count -eq 1) 'checkpoint protected and inventoried'
    Refuse { & $tool -BackupRoot $root -RemovePreChange -BackupPath $b.Path -ExpectedSha256 $b.Sha -ConfirmRemoval -ConfirmChangeAccepted -ChangeAcceptanceEvidence 'done' } 'sole recovery backup deletion'
    Refuse { & $tool -BackupRoot $root -RemoveSuperseded -BackupPath $b.Path -ExpectedSha256 $b.Sha -ConfirmRemoval } 'active checkpoint is not superseded'

    # Tampered data and incomplete restore metadata prevent promotion.
    $tampered = Make-Dump 'tampered.dump' 'tamper'
    [IO.File]::AppendAllText($tampered.Path, 'changed')
    Refuse { & $tool -BackupRoot $root -RegisterPreChange -BackupPath $tampered.Path -ExpectedSha256 $tampered.Sha -ChangeReference 'x' } 'hash mismatch'
    $badReport = Make-Dump 'badreport.dump' 'badreport'
    $report = Get-Content -LiteralPath "$($badReport.Path).restore-verification.json" -Raw | ConvertFrom-Json -AsHashtable
    $report.passed = $false
    $report | ConvertTo-Json | Set-Content -LiteralPath "$($badReport.Path).restore-verification.json" -Encoding utf8
    Refuse { & $tool -BackupRoot $root -SetRecoveryCheckpoint -BackupPath $badReport.Path -ExpectedSha256 $badReport.Sha -ConfirmIndependentCopy -RemoteSha256 $badReport.Sha -RemoteConfirmation 'checked off device' } 'failed restore cannot become checkpoint'

    & $tool -BackupRoot $root -SetRecoveryCheckpoint -BackupPath $c.Path -ExpectedSha256 $c.Sha -ConfirmIndependentCopy -RemoteSha256 $c.Sha -RemoteConfirmation 'second remote backup retrieved and hashed'
    $list = @(& $tool -BackupRoot $root)
    Check (@($list | Where-Object { $_.Name -eq 'checkpoint-b.dump' -and $_.Role -eq 'superseded-checkpoint' }).Count -eq 1) 'former checkpoint is held as superseded rather than deleted'
    Check (@($list | Where-Object { $_.Name -eq 'checkpoint-c.dump' -and $_.Role -eq 'recovery-checkpoint' }).Count -eq 1) 'new checkpoint protected'
    Refuse { & $tool -BackupRoot $root -RemoveSuperseded -BackupPath $b.Path -ExpectedSha256 $b.Sha } 'superseded removal requires explicit confirmation'
    Check ((Test-Path -LiteralPath $b.Path) -and (Test-Path -LiteralPath $c.Path)) 'refresh alone does not delete either backup'

    # A missing/new corrupted protected backup must block retirement of old.
    Rename-Item -LiteralPath "$($c.Path).json" -NewName 'checkpoint-c.json.held'
    Refuse { & $tool -BackupRoot $root -RemoveSuperseded -BackupPath $b.Path -ExpectedSha256 $b.Sha -ConfirmRemoval } 'missing protected replacement manifest'
    Check (Test-Path -LiteralPath $b.Path) 'previous checkpoint survives failed refresh inspection'
    Rename-Item -LiteralPath (Join-Path $root 'checkpoint-c.json.held') -NewName 'checkpoint-c.dump.json'

    & $tool -BackupRoot $root -RemoveSuperseded -BackupPath $b.Path -ExpectedSha256 $b.Sha -ConfirmRemoval
    Check (-not (Test-Path -LiteralPath $b.Path)) 'superseded dump deleted only after new verified recovery exists'
    Check (-not (Test-Path -LiteralPath "$($b.Path).json")) 'superseded hash manifest deleted'
    Check (-not (Test-Path -LiteralPath "$($b.Path).restore-verification.json")) 'superseded restore report deleted'
    Check (Test-Path -LiteralPath $c.Path) 'new checkpoint remains protected'

    & $tool -BackupRoot $root -RemovePreChange -BackupPath $a.Path -ExpectedSha256 $a.Sha -ConfirmRemoval -ConfirmChangeAccepted -ChangeAcceptanceEvidence 'migration-1 post-change application and schema verification passed'
    Check (-not (Test-Path -LiteralPath $a.Path)) 'accepted pre-change dump explicitly retired'
    Check (-not (Test-Path -LiteralPath "$($a.Path).json")) 'accepted pre-change manifest explicitly retired'
    Check (-not (Test-Path -LiteralPath "$($a.Path).restore-verification.json")) 'associated verification evidence explicitly retired'
    Check (Test-Path -LiteralPath $c.Path) 'protected recovery unaffected by pre-change cleanup'

    $list = @(& $tool -BackupRoot $root)
    Check (@($list | Where-Object Role -eq 'recovery-checkpoint').Count -eq 1) 'one current checkpoint remains'
    Check (@($list | Where-Object Role -eq 'unclassified').Count -ge 1) 'unknown dumps remain untouched'

    Write-Host "WI-0209 disposable backup-retention assertions: $script:assertions passed."
}
finally {
    Remove-Item -LiteralPath $root -Force -Recurse -ErrorAction SilentlyContinue
}
