# Shared conservative PostgreSQL restore-copy ownership and cleanup helpers.
# Dot-source from the verifier or operator inventory tool only.
Set-StrictMode -Version Latest

function Test-GeneratedRestoreName {
    param([string]$Name)
    return -not [string]::IsNullOrWhiteSpace($Name) -and
        $Name -cmatch '^photoidentity_restore_[0-9]{14}_[a-f0-9]{6}$'
}

function Test-RestoreOwnershipMarker {
    param([string]$Marker)
    return -not [string]::IsNullOrWhiteSpace($Marker) -and
        $Marker -cmatch '^photoidentity-restore:v1:[a-f0-9]{32}:[a-f0-9]{64}$'
}

function Invoke-RestoreAdminSql {
    param([Parameter(Mandatory = $true)][string]$Sql)
    if ($null -eq $script:podman -or [string]::IsNullOrWhiteSpace($script:containerId)) {
        throw 'The PostgreSQL container must be resolved before querying restore ownership.'
    }
    $rows = @(& $script:podman.Source exec -e "RESTORE_SQL=$Sql" $script:containerId sh -lc 'psql -X -A -t -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -c "$RESTORE_SQL"' 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw 'PostgreSQL restore ownership query failed; no database was removed.'
    }
    return @($rows | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -ne '' })
}

function Get-RestoreDatabaseDetails {
    param([Parameter(Mandatory = $true)][string]$Name)
    if (-not (Test-GeneratedRestoreName $Name)) {
        throw 'Only exact generated photoidentity_restore_YYYYMMDDHHMMSS_xxxxxx names are eligible.'
    }
    $sql = "SELECT COALESCE(shobj_description(oid, 'pg_database'), '') || '|' || pg_database_size(oid)::text || '|' || (SELECT COUNT(*) FROM pg_stat_activity WHERE datname = '$Name')::text FROM pg_database WHERE datname = '$Name';"
    $rows = @(Invoke-RestoreAdminSql -Sql $sql)
    if ($rows.Count -eq 0) { return $null }
    if ($rows.Count -ne 1) {
        throw 'Ambiguous restore database lookup; refusing cleanup.'
    }
    $fields = @(([string]$rows[0]).Split('|'))
    if ($fields.Count -ne 3) {
        throw 'Unexpected restore provenance metadata; refusing cleanup.'
    }
    $size = [long]0
    $sessions = [int]0
    if (-not [long]::TryParse($fields[1], [ref]$size) -or
        -not [int]::TryParse($fields[2], [ref]$sessions) -or $size -lt 0 -or $sessions -lt 0) {
        throw 'Invalid PostgreSQL storage/session evidence; refusing cleanup.'
    }
    return [pscustomobject]@{
        Name = $Name
        Marker = $fields[0]
        Bytes = $size
        Sessions = $sessions
        Owned = (Test-RestoreOwnershipMarker $fields[0])
    }
}

# Diagnostic-only metadata: backend kinds and aggregate counts, never PID,
# role, application name, SQL text, file path, or catalogue contents.
# This function neither blocks, terminates nor changes a connection.
function Get-RestoreSessionSummary {
    param([Parameter(Mandatory = $true)][string]$Name)
    if (-not (Test-GeneratedRestoreName $Name)) {
        throw 'Only exact generated restore names may be inspected.'
    }
    # Categories are fixed on the server before any diagnostic values are
    # returned. Unrecognized extension worker labels collapse to 'other'.
    $sql = "SELECT CASE WHEN backend_type IN ('client backend','autovacuum worker','parallel worker','background worker','logical replication worker') THEN backend_type ELSE 'other' END || '|' || COUNT(*)::text FROM pg_stat_activity WHERE datname = '$Name' GROUP BY 1 ORDER BY 1;"
    $rows = @(Invoke-RestoreAdminSql -Sql $sql)
    foreach ($line in $rows) {
        $parts = @(([string]$line).Split('|'))
        $count = [int]0
        if ($parts.Count -ne 2 -or
            $parts[0] -cnotin @('client backend','autovacuum worker','parallel worker','background worker','logical replication worker','other') -or
            -not [int]::TryParse($parts[1], [ref]$count) -or $count -le 0) {
            throw 'Unexpected PostgreSQL session summary; no identities were returned.'
        }
        [pscustomobject]@{ backendType = $parts[0]; sessions = $count }
    }
}

function Set-RestoreDatabaseOwnership {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Marker
    )
    if (-not (Test-GeneratedRestoreName $Name) -or -not (Test-RestoreOwnershipMarker $Marker)) {
        throw 'Invalid generated restore identity; ownership was not recorded.'
    }
    # Both identifiers are strictly validated before interpolating them into SQL.
    $null = @(Invoke-RestoreAdminSql -Sql "COMMENT ON DATABASE $Name IS '$Marker';")
    $details = Get-RestoreDatabaseDetails -Name $Name
    if ($null -eq $details -or $details.Marker -cne $Marker) {
        throw 'Could not establish exact ownership of the new restore database.'
    }
}


function Find-ExistingOwnedRestoresForHash {
    param([Parameter(Mandatory = $true)][string]$BackupSha256)
    if ($BackupSha256 -cnotmatch '^[a-f0-9]{64}$') {
        throw 'Invalid backup fingerprint; refusing another restore.'
    }

    # Inspect database metadata only. Any previously retained copy for this
    # backup must be explicitly resumed or retired before trying another.
    $sql = "SELECT datname || '|' || COALESCE(shobj_description(oid, 'pg_database'), '') FROM pg_database WHERE datname ~ '^photoidentity_restore_[0-9]{14}_[a-f0-9]{6}$' ORDER BY datname;"
    $rows = @(Invoke-RestoreAdminSql -Sql $sql)
    $names = @(
        foreach ($line in $rows) {
            $parts = @(([string]$line).Split('|'))
            if ($parts.Count -ne 2 -or -not (Test-GeneratedRestoreName $parts[0])) {
                throw 'Ambiguous restore-provenance inventory; refusing another restore.'
            }
            if ((Test-RestoreOwnershipMarker $parts[1]) -and
                $parts[1].EndsWith(":$BackupSha256", [StringComparison]::Ordinal)) {
                $parts[0]
            }
        }
    )
    return @($names)
}

function Invoke-RestoreDrop {
    param([Parameter(Mandatory = $true)][string]$Name)
    if (-not (Test-GeneratedRestoreName $Name)) {
        throw 'Refusing drop of a non-generated database name.'
    }
    & $script:podman.Source exec -e "VERIFY_DATABASE=$Name" $script:containerId sh -lc 'dropdb -U "$POSTGRES_USER" "$VERIFY_DATABASE"' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL refused to drop isolated restore database '$Name'."
    }
}

function Remove-OwnedRestoreDatabase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ExpectedMarker,
        [Parameter(Mandatory = $true)][string]$SourceDatabase,
        [ValidateRange(1, 20)][int]$SessionWaitAttempts = 1,
        [ValidateRange(0, 5000)][int]$SessionWaitMilliseconds = 0
    )
    if (-not (Test-GeneratedRestoreName $Name) -or
        -not (Test-RestoreOwnershipMarker $ExpectedMarker) -or
        [string]::IsNullOrWhiteSpace($SourceDatabase) -or
        $Name -ceq $SourceDatabase) {
        throw 'Restore cleanup protection failed (name, marker, or source database).'
    }

    # A finished pg_restore may briefly leave a PostgreSQL connection or
    # background worker visible. Automatic cleanup can wait for it to exit,
    # but every observation must recheck exact ownership. Operator cleanup
    # retains the original immediate refusal (one attempt by default).
    for ($attempt = 1; $attempt -le $SessionWaitAttempts; $attempt++) {
        $details = Get-RestoreDatabaseDetails -Name $Name
        if ($null -eq $details -or -not $details.Owned -or $details.Marker -cne $ExpectedMarker) {
            throw "Database '$Name' is absent or has missing/mismatched ownership; no drop was attempted."
        }
        if ($details.Sessions -eq 0) {
            # dropdb independently refuses sessions that arrive after this
            # observation. Never terminate connections or use --force.
            Invoke-RestoreDrop -Name $Name
            if ($null -ne (Get-RestoreDatabaseDetails -Name $Name)) {
                throw "PostgreSQL still reports isolated restore database '$Name' after cleanup."
            }
            return
        }
        if ($attempt -lt $SessionWaitAttempts) {
            if ($attempt -eq 1) {
                Write-Host "Restore cleanup detected active sessions; waiting briefly for them to exit (no connections will be terminated)."
            }
            Start-Sleep -Milliseconds $SessionWaitMilliseconds
        }
    }
    throw "Database '$Name' has active sessions after $SessionWaitAttempts checks; no connections were terminated and no drop was attempted."
}

function Write-RestoreVerificationReport {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Report
    )
    $temporary = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $Report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $temporary -Encoding UTF8 -ErrorAction Stop
        # The temporary file is fully written and closed first; overwriting
        # via a same-directory rename avoids the platform-specific requirement
        # for a non-null backup path in File.Replace.
        [IO.File]::Move($temporary, $Path, $true)
    }
    finally {
        Remove-Item -LiteralPath $temporary -ErrorAction SilentlyContinue
    }
}
