[CmdletBinding()]
param(
    [string]$BackupRoot,
    [string]$BackupPath,
    [string]$ExpectedSha256,
    [switch]$RegisterPreChange,
    [switch]$SetRecoveryCheckpoint,
    [switch]$RemovePreChange,
    [switch]$RemoveSuperseded,
    [switch]$PlanRemoval,
    [string]$ChangeReference,
    [string]$ChangeAcceptanceEvidence,
    [switch]$ConfirmChangeAccepted,
    [string]$RemoteConfirmation,
    [string]$RemoteSha256,
    [switch]$ConfirmIndependentCopy,
    [switch]$ConfirmRemoval
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-NoReparse {
    param([Parameter(Mandatory)][string]$Path)
    # Reject linked ancestors as well as online-only/reparse files. A path
    # resolving through a junction must never broaden this deletion boundary.
    $cursor = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'A reparse-point path is not eligible for backup retention operations.'
            }
        }
        $parent = [IO.Directory]::GetParent($cursor)
        if ($null -eq $parent) { break }
        $cursor = $parent.FullName
    }
}

function Assert-Sha {
    param([string]$Value)
    if ($Value -cnotmatch '^[a-fA-F0-9]{64}$') {
        throw 'An exact 64-character SHA-256 fingerprint is required.'
    }
    return $Value.ToLowerInvariant()
}

function Assert-BackupName {
    param([string]$Name)
    if ($Name -cnotmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]*\.dump$' -or $Name.Contains('..')) {
        throw 'Only simple exact .dump file names without path traversal are eligible.'
    }
}

function Resolve-Backup {
    param([Parameter(Mandatory)][string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    $name = [IO.Path]::GetFileName($full)
    Assert-BackupName $name
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($full), $script:root, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Backup must be an immediate child of the approved backup root.'
    }
    Assert-NoReparse $full
    return [pscustomobject]@{ Name = $name; Path = $full }
}

function Test-FileHere {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    Assert-NoReparse $Path
    return $true
}

function Read-Json {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-FileHere $Path)) { throw 'Required backup metadata or file is missing.' }
    return (Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -AsHashtable -ErrorAction Stop)
}

function Get-Evidence {
    param([Parameter(Mandatory)]$Backup, [switch]$RequireRestore)
    if (-not (Test-FileHere $Backup.Path)) { throw 'The exact backup dump does not exist.' }
    $manifest = Read-Json "$($Backup.Path).json"
    if ($manifest.format -cne 'pg_dump-custom' -or
        [string]::IsNullOrWhiteSpace([string]$manifest.sourceDatabase) -or
        -not [string]::Equals([string]$manifest.backupPath, $Backup.Path, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Backup manifest has unexpected format, source or exact path.'
    }
    $actualHash = (Get-FileHash -LiteralPath $Backup.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $declaredHash = Assert-Sha ([string]$manifest.backupSha256)
    if ($actualHash -cne $declaredHash -or [long](Get-Item -LiteralPath $Backup.Path).Length -ne [long]$manifest.backupBytes) {
        throw 'Backup hash or length does not match its manifest.'
    }
    $restorePath = "$($Backup.Path).restore-verification.json"
    if (Test-Path -LiteralPath $restorePath) {
        $verification = Read-Json $restorePath
        if ((Assert-Sha ([string]$verification.backupSha256)) -cne $actualHash) {
            throw 'Restore verification report refers to a different backup.'
        }
    }
    elseif ($RequireRestore) {
        throw 'A passing restore verification report is required before recovery-checkpoint promotion.'
    }
    if ($RequireRestore -and
        ($verification.passed -ne $true -or $verification.cleanupStatus -cne 'removed' -or $verification.cleanupRequired -ne $false)) {
        throw 'Recovery checkpoint requires a fully verified restore and completed isolated cleanup.'
    }
    return $actualHash
}

function Read-State {
    if (-not (Test-Path -LiteralPath $script:statePath)) {
        return @{ schemaVersion = 1; recoveryCheckpoint = $null; preChanges = @(); superseded = @() }
    }
    $state = Read-Json $script:statePath
    if ($state.schemaVersion -ne 1 -or
        -not $state.ContainsKey('recoveryCheckpoint') -or
        -not $state.ContainsKey('preChanges') -or
        -not $state.ContainsKey('superseded') -or
        $null -eq $state.preChanges -or $null -eq $state.superseded) {
        throw 'Unknown or incomplete backup retention state; refusing changes.'
    }
    $all = @($state.preChanges) + @($state.superseded)
    if ($null -ne $state.recoveryCheckpoint) { $all += $state.recoveryCheckpoint }
    $names = @{}
    foreach ($entry in $all) {
        $name = [string]$entry.name
        Assert-BackupName $name
        $hash = Assert-Sha ([string]$entry.sha256)
        if ($names.ContainsKey($name)) { throw 'Ambiguous duplicate backup retention roles.' }
        $names[$name] = $hash
    }
    return $state
}

function Save-State {
    param($State)
    $temp = Join-Path $script:root ('.retention-state-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $State | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temp -Encoding utf8 -ErrorAction Stop
        [IO.File]::Move($temp, $script:statePath, $true)
    }
    finally {
        Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
    }
}

function Get-Role {
    param($State, [string]$Name)
    if ($null -ne $State.recoveryCheckpoint -and $State.recoveryCheckpoint.name -eq $Name) {
        return 'recovery-checkpoint'
    }
    if (@($State.preChanges | Where-Object { $_.name -eq $Name }).Count -gt 0) { return 'pre-change' }
    if (@($State.superseded | Where-Object { $_.name -eq $Name }).Count -gt 0) { return 'superseded-checkpoint' }
    return 'unclassified'
}

function Remove-SelectedBackup {
    param($Backup, [string]$Hash)
    # Revalidate all three paths and their relationship before any deletion.
    $sidecars = @("$($Backup.Path).restore-verification.json", "$($Backup.Path).json")
    foreach ($path in @($Backup.Path) + $sidecars) {
        if (Test-Path -LiteralPath $path) { Assert-NoReparse $path }
    }
    if ((Get-Evidence -Backup $Backup) -cne $Hash) {
        throw 'Exact expected SHA-256 no longer matches; nothing deleted.'
    }
    # Delete metadata first, dump last. Any failure is surfaced immediately;
    # state is deliberately not updated until all files have been removed.
    foreach ($path in $sidecars) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction Stop }
    }
    Remove-Item -LiteralPath $Backup.Path -Force -ErrorAction Stop
}

if ([string]::IsNullOrWhiteSpace($BackupRoot)) {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw 'LOCALAPPDATA is not available. Supply an explicitly approved -BackupRoot.'
    }
    $BackupRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\backups\postgresql'
}
$script:root = [IO.Path]::GetFullPath($BackupRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $script:root -PathType Container)) {
    throw 'Approved backup root does not exist; refusing to create it implicitly.'
}
if ([IO.Path]::GetPathRoot($script:root) -eq $script:root) {
    throw 'A volume root cannot be used as a backup-management root.'
}
Assert-NoReparse $script:root
$script:statePath = Join-Path $script:root 'retention-state.json'
# Exactly one mutating mode is permitted.
$count = [int]$RegisterPreChange.IsPresent + [int]$SetRecoveryCheckpoint.IsPresent +
    [int]$RemovePreChange.IsPresent + [int]$RemoveSuperseded.IsPresent + [int]$PlanRemoval.IsPresent
if ($count -gt 1) { throw 'Select exactly one backup-management action.' }

# One operator transition at a time. Read-only inventory/plan creates no
# lockfile and never writes; a concurrent mutation fails without modifying
# registry state, role assignments or any dump.
$mutationLock = $null
if ($count -gt 0 -and -not $PlanRemoval) {
    $lockPath = Join-Path $script:root 'retention-state.lock'
    Assert-NoReparse $lockPath
    try {
        $mutationLock = [IO.File]::Open(
            $lockPath, [IO.FileMode]::OpenOrCreate,
            [IO.FileAccess]::ReadWrite, [IO.FileShare]::None
        )
    }
    catch {
        throw 'Another backup-retention operation holds the registry lock; no action was taken.'
    }
}
try {
$state = Read-State

if ($count -eq 0) {
    foreach ($item in @(Get-ChildItem -LiteralPath $script:root -Filter '*.dump' -File -ErrorAction Stop)) {
        [pscustomobject]@{
            Name = $item.Name
            Role = Get-Role -State $state -Name $item.Name
            MiB = [math]::Round($item.Length / 1MB, 2)
            ManifestPresent = (Test-Path -LiteralPath "$($item.FullName).json" -PathType Leaf)
            RestoreReportPresent = (Test-Path -LiteralPath "$($item.FullName).restore-verification.json" -PathType Leaf)
            ReparsePoint = (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
        }
    }
    if ($null -ne $state.recoveryCheckpoint -and
        -not (Test-Path -LiteralPath (Join-Path $script:root $state.recoveryCheckpoint.name) -PathType Leaf)) {
        Write-Warning 'The recorded recovery checkpoint is missing from the approved local root.'
    }
    return
}

if ([string]::IsNullOrWhiteSpace($BackupPath)) { throw 'Select exactly one backup using -BackupPath.' }
$backup = Resolve-Backup -Path $BackupPath
$expected = Assert-Sha $ExpectedSha256
$role = Get-Role -State $state -Name $backup.Name
$hash = Get-Evidence -Backup $backup -RequireRestore:$SetRecoveryCheckpoint
if ($hash -cne $expected) { throw 'Operator-expected backup SHA-256 does not match the file.' }

if ($PlanRemoval) {
    if ($role -notin @('pre-change', 'superseded-checkpoint')) {
        throw 'Only explicitly classified, non-current backups can be planned for retirement.'
    }
    [pscustomobject]@{
        Name = $backup.Name
        Role = $role
        Sha256 = $hash
        RemovalRequiresConfirmation = $true
        Files = @(@(
            $backup.Path,
            "$($backup.Path).json",
            "$($backup.Path).restore-verification.json"
        ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    }
    return
}

if ($RegisterPreChange) {
    if ($role -cne 'unclassified') { throw 'Classified backups cannot be registered again or change roles implicitly.' }
    if ([string]::IsNullOrWhiteSpace($ChangeReference)) {
        throw 'A nonempty operator change reference is required.'
    }
    $state.preChanges = @($state.preChanges) + @(@{
        name = $backup.Name; sha256 = $hash; changeReference = $ChangeReference
        registeredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    })
    Save-State $state
    Write-Host "Registered exact pre-change backup '$($backup.Name)' without deleting files."
    return
}

if ($SetRecoveryCheckpoint) {
    if ($role -eq 'superseded-checkpoint') {
        throw 'Superseded checkpoints must not be silently promoted again.'
    }
    if (-not $ConfirmIndependentCopy -or [string]::IsNullOrWhiteSpace($RemoteConfirmation) -or
        (Assert-Sha $RemoteSha256) -cne $hash) {
        throw 'Require independent remote availability confirmation and matching remote SHA-256 evidence.'
    }
    if ($null -ne $state.recoveryCheckpoint -and $state.recoveryCheckpoint.name -ne $backup.Name) {
        if ($state.recoveryCheckpoint.sha256 -ceq $hash) {
            throw 'Replacement checkpoint must have a distinct verified backup fingerprint.'
        }
        $state.superseded = @($state.superseded) + @($state.recoveryCheckpoint)
    }
    $state.preChanges = @($state.preChanges | Where-Object { $_.name -ne $backup.Name })
    $state.recoveryCheckpoint = @{
        name = $backup.Name; sha256 = $hash
        remoteConfirmation = $RemoteConfirmation
        remoteSha256 = $hash; confirmedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    Save-State $state
    Write-Host "Recorded independently confirmed recovery checkpoint '$($backup.Name)'. Previous checkpoint, if any, is retained as superseded."
    return
}

if (-not $ConfirmRemoval) { throw 'Exact-file removal requires -ConfirmRemoval.' }
if ($RemovePreChange) {
    if ($role -cne 'pre-change') { throw 'Only explicitly registered pre-change backups may use this removal mode.' }
    if (-not $ConfirmChangeAccepted -or [string]::IsNullOrWhiteSpace($ChangeAcceptanceEvidence)) {
        throw 'Require explicit post-change acceptance and a nonempty local verification reference.'
    }
    Remove-SelectedBackup -Backup $backup -Hash $hash
    $state.preChanges = @($state.preChanges | Where-Object { $_.name -ne $backup.Name })
}
elseif ($RemoveSuperseded) {
    if ($role -cne 'superseded-checkpoint') {
        throw 'Only explicitly superseded checkpoints may use this removal mode.'
    }
    if ($null -eq $state.recoveryCheckpoint -or $state.recoveryCheckpoint.name -eq $backup.Name -or
        [string]::IsNullOrWhiteSpace([string]$state.recoveryCheckpoint.remoteConfirmation)) {
        throw 'A distinct, independently confirmed current recovery checkpoint is required.'
    }
    $current = Resolve-Backup -Path (Join-Path $script:root $state.recoveryCheckpoint.name)
    if ((Get-Evidence -Backup $current -RequireRestore) -cne $state.recoveryCheckpoint.sha256) {
        throw 'Current protected recovery checkpoint cannot be revalidated.'
    }
    Remove-SelectedBackup -Backup $backup -Hash $hash
    $state.superseded = @($state.superseded | Where-Object { $_.name -ne $backup.Name })
}
else { throw 'Unknown operation.' }
Save-State $state
Write-Host "Removed only explicitly selected backup '$($backup.Name)' and related metadata; active recovery checkpoint remains protected."

}
finally {
    if ($null -ne $mutationLock) { $mutationLock.Dispose() }
}
