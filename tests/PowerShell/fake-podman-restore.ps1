# In-process fake Podman for the source-free WI-0208 verifier integration tests.
# Never runs Podman, shell, PostgreSQL or any destructive operation.
Set-StrictMode -Version Latest
$global:LASTEXITCODE = 0
$words = @($args)
if ($words.Count -ge 3 -and $words[0] -eq 'compose' -and $words[1] -eq 'ps') {
    'synthetic-container'
    return
}
if ($words.Count -ge 2 -and $words[0] -eq 'cp') {
    $global:FakeRestore.Copies++
    return
}
if ($words.Count -lt 2 -or $words[0] -ne 'exec') {
    throw 'Fake Podman saw an unsupported command.'
}
$envFlags = @{}
for ($i = 1; $i -lt $words.Count - 1; $i++) {
    if ($words[$i] -eq '-e') {
        $pair = $words[$i + 1].Split('=', 2)
        if ($pair.Count -ne 2) { throw 'Bad fake Podman environment parameter.' }
        $envFlags[$pair[0]] = $pair[1]
        $i++
    }
}
$cmd = [string]$words[-1]
if ($cmd -like '*createdb *') {
    $global:FakeRestore.Created++
    $global:FakeRestore.Exists = $true
    $global:FakeRestore.Name = $envFlags.VERIFY_DATABASE
    return
}
if ($cmd -like '*pg_restore *') {
    $global:FakeRestore.Restores++
    if ($global:FakeRestore.Interrupts) { throw [System.OperationCanceledException]::new('synthetic interruption') }
    if ($global:FakeRestore.RestoreFails) { $global:LASTEXITCODE = 1; return }
    return
}
if ($cmd -like '*dropdb *') {
    $global:FakeRestore.DropAttempts++
    if ($global:FakeRestore.DropFails) { $global:LASTEXITCODE = 1; return }
    $global:FakeRestore.Exists = $false
    return
}
if ($cmd -like '*rm -f *') {
    $global:FakeRestore.TempCleans++
    return
}
if ($envFlags.ContainsKey('PHOTOIDENTITY_SQL')) {
    $sql = $envFlags.PHOTOIDENTITY_SQL
    $database = $envFlags.TARGET_DATABASE
    if ($database -like 'photoidentity_restore_*' -and -not $global:FakeRestore.Exists) {
        $global:LASTEXITCODE = 1
        return
    }
    if ($sql -like 'SELECT COALESCE(MAX(version)*') { '34'; return }
    if ($sql -like 'SELECT tablename FROM pg_catalog.pg_tables*') { 'synthetic_table'; return }
    if ($sql -like 'SELECT COUNT(*) FROM public.*') {
        if ($database -like 'photoidentity_restore_*' -and $global:FakeRestore.CompareMismatch) { '2' } else { '1' }
        return
    }
    if ($sql -like '*pg_catalog.pg_constraint*') { '0'; return }
    if ($sql -like 'SELECT pg_database_size(current_database())*') { '8192'; return }
    throw "Fake unsupported catalogue SQL: $sql"
}
if ($envFlags.ContainsKey('RESTORE_SQL')) {
    $sql = $envFlags.RESTORE_SQL
    if ($sql -like 'COMMENT ON DATABASE *') {
        if (-not $global:FakeRestore.Exists) { $global:LASTEXITCODE = 1; return }
        $match = [regex]::Match($sql, " IS '([^']+)';$")
        if (-not $match.Success) { throw 'Bad synthetic ownership SQL.' }
        $global:FakeRestore.Marker = $match.Groups[1].Value
        return
    }
    if ($sql -like 'SELECT COALESCE(shobj_description*') {
        if ($global:FakeRestore.Exists) {
            $sessions = $global:FakeRestore.Sessions
            if ($global:FakeRestore.Restores -gt 0 -and
                $global:FakeRestore.TransientSessionsRemaining -gt 0) {
                $global:FakeRestore.TransientSessionsRemaining--
                $sessions = 1
            }
            "$($global:FakeRestore.Marker)|8192|$sessions"
        }
        return
    }
    if ($sql -like 'SELECT datname*') {
        if ($global:FakeRestore.Exists) {
            if ($sql -like '*pg_database_size*') {
                "$($global:FakeRestore.Name)|$($global:FakeRestore.Marker)|8192|$($global:FakeRestore.Sessions)"
            }
            else {
                "$($global:FakeRestore.Name)|$($global:FakeRestore.Marker)"
            }
        }
        return
    }
    throw "Fake unsupported administrator SQL: $sql"
}
if ($cmd -like '*SELECT 1 FROM pg_database*') {
    if ($global:FakeRestore.Exists) { '1' }
    return
}
throw "Fake Podman saw an unsupported executable: $cmd"
