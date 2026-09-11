[CmdletBinding()]
param(
    [string]$ConnectionEnvironmentVariable = "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING",
    [string]$EnvironmentPath = (Join-Path $PSScriptRoot "deploy\postgres\.env"),
    [string]$OutputPath,
    [ValidateRange(1, 200)]
    [int]$Limit = 40,
    [ValidateRange(5, 300)]
    [int]$StatementTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Read-DotEnv {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "PostgreSQL environment file was not found."
    }

    $settings = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $separator = $trimmed.IndexOf("=")
        if ($separator -le 0) {
            continue
        }

        $name = $trimmed.Substring(0, $separator).Trim()
        $value = $trimmed.Substring($separator + 1).Trim()
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
            ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        $settings[$name] = $value
    }

    return $settings
}

function Get-ConnectionValue {
    param(
        [Parameter(Mandatory)][System.Data.Common.DbConnectionStringBuilder]$Builder,
        [Parameter(Mandatory)][string[]]$Names
    )

    foreach ($name in $Names) {
        if ($Builder.ContainsKey($name)) {
            return [string]$Builder[$name]
        }
    }

    return $null
}

$connectionString = [Environment]::GetEnvironmentVariable(
    $ConnectionEnvironmentVariable,
    "Process")
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    $connectionString = [Environment]::GetEnvironmentVariable(
        $ConnectionEnvironmentVariable,
        "User")
}
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw "The PostgreSQL connection environment variable '$ConnectionEnvironmentVariable' is not set at Process or User scope."
}

$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder.ConnectionString = $connectionString
$databaseName = Get-ConnectionValue -Builder $builder -Names @("Database", "Initial Catalog")
$username = Get-ConnectionValue -Builder $builder -Names @("Username", "User ID", "UserID", "User Id")
if ([string]::IsNullOrWhiteSpace($databaseName)) {
    throw "The configured PostgreSQL connection string does not name a database."
}
if ([string]::IsNullOrWhiteSpace($username)) {
    throw "The configured PostgreSQL connection string does not name a user."
}
if ($databaseName -notmatch '^[A-Za-z0-9_]+$') {
    throw "The configured PostgreSQL database name contains unsupported characters for this diagnostic."
}

$settings = Read-DotEnv -Path $EnvironmentPath
if (-not $settings.ContainsKey("PHOTOIDENTITY_POSTGRES_USER")) {
    throw "PHOTOIDENTITY_POSTGRES_USER is missing from the PostgreSQL environment file."
}
if ($username -cne [string]$settings["PHOTOIDENTITY_POSTGRES_USER"]) {
    throw "The configured runtime PostgreSQL user does not match deploy/postgres/.env. The plan probe refuses to pass credentials on the command line."
}

$composePath = Join-Path $PSScriptRoot "deploy\postgres\compose.yaml"
if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) {
    throw "PostgreSQL compose.yaml was not found."
}
if (-not (Get-Command podman -ErrorAction SilentlyContinue)) {
    throw "Podman is required for the PostgreSQL gallery plan probe."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $reportDirectory = Join-Path ([IO.Path]::GetTempPath()) "PhotoIdentity\postgres-gallery-plan"
    [IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
    $OutputPath = Join-Path $reportDirectory ("gallery-plan-{0}.txt" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}
else {
    $OutputPath = [IO.Path]::GetFullPath($OutputPath)
    $reportDirectory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        [IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
    }
}

$timeoutMilliseconds = $StatementTimeoutSeconds * 1000
$sql = @"
\set ON_ERROR_STOP on
SET statement_timeout = '${timeoutMilliseconds}ms';
SET lock_timeout = '5s';

SELECT model_id, model_hash
FROM identity_suggestion_rankings
WHERE rank = 1
GROUP BY model_id, model_hash
ORDER BY max(generated_at_utc) DESC, model_id, model_hash
LIMIT 1
\gset gallery_

SELECT
    high_score_threshold,
    high_margin_threshold,
    medium_score_threshold
FROM identity_suggestion_policies
WHERE model_id = :'gallery_model_id'
  AND model_hash = :'gallery_model_hash'
\gset gallery_

\echo === catalogue-context ===
SELECT 'schema-version=' || COALESCE(MAX(version), 0)
FROM photo_identity_schema_migrations;
SELECT 'face-occurrences=' || COUNT(*)
FROM face_occurrences;
SELECT 'rank-one-suggestions=' || COUNT(*)
FROM identity_suggestion_rankings
WHERE rank = 1
  AND model_id = :'gallery_model_id'
  AND model_hash = :'gallery_model_hash';
SELECT 'active-review-actions=' || COUNT(*)
FROM review_actions
WHERE action_kind IN ('assign', 'unknown', 'reject')
  AND reversed_at_utc IS NULL;
\echo model-id=:gallery_model_id
\echo model-hash=:gallery_model_hash
\echo page-limit=$Limit

\echo === needs-review-suggested-person-page ===
EXPLAIN (ANALYZE, BUFFERS, WAL, TIMING FALSE, SUMMARY TRUE, FORMAT JSON)
WITH top_suggestion AS (
    SELECT
        rankings.face_occurrence_id,
        suggestions.id AS suggestion_id,
        suggestions.suggested_person_id,
        suggested_people.display_name,
        rankings.model_id,
        rankings.model_hash,
        rankings.rank,
        suggestions.score,
        rankings.score_margin,
        suggestions.status,
        rankings.generated_at_utc
    FROM identity_suggestion_rankings AS rankings
    INNER JOIN identity_suggestions AS suggestions
        ON suggestions.id = rankings.suggestion_id
    INNER JOIN people AS suggested_people
        ON suggested_people.id = suggestions.suggested_person_id
       AND suggested_people.merged_into_person_id IS NULL
    WHERE rankings.rank = 1
      AND suggestions.status = 'pending'
      AND rankings.model_id = :'gallery_model_id'
      AND rankings.model_hash = :'gallery_model_hash'
),
candidate_faces AS (
    SELECT
        face_occurrences.id,
        face_occurrences.ordinal,
        face_occurrences.created_at_utc,
        face_occurrences.asset_revision_id,
        latest_action.id AS review_action_id,
        latest_action.action_kind AS review_action_kind,
        latest_action.person_id AS review_person_id,
        top_suggestion.suggestion_id,
        top_suggestion.suggested_person_id,
        top_suggestion.display_name AS suggested_person_name,
        top_suggestion.model_id AS suggestion_model_id,
        top_suggestion.model_hash AS suggestion_model_hash,
        top_suggestion.rank AS suggestion_rank,
        top_suggestion.score AS suggestion_score,
        top_suggestion.score_margin AS suggestion_score_margin,
        top_suggestion.status AS suggestion_status,
        top_suggestion.generated_at_utc AS suggestion_generated_at_utc
    FROM face_occurrences
    LEFT JOIN LATERAL (
        SELECT review_actions.id, review_actions.action_kind, review_actions.person_id
        FROM review_actions
        WHERE review_actions.face_occurrence_id = face_occurrences.id
          AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
          AND review_actions.reversed_at_utc IS NULL
        ORDER BY review_actions.id DESC
        LIMIT 1
    ) AS latest_action ON TRUE
    LEFT JOIN top_suggestion
        ON top_suggestion.face_occurrence_id = face_occurrences.id
    WHERE latest_action.id IS NULL
    ORDER BY
        CASE WHEN top_suggestion.suggestion_id IS NULL THEN 1 ELSE 0 END,
        lower(top_suggestion.display_name),
        top_suggestion.suggested_person_id,
        top_suggestion.score_margin DESC,
        top_suggestion.score DESC,
        face_occurrences.created_at_utc DESC,
        face_occurrences.id
    LIMIT $Limit OFFSET 0
)
SELECT
    face_occurrences.id,
    face_occurrences.ordinal,
    face_occurrences.created_at_utc,
    assets.source_key,
    COALESCE(asset_revisions.media_type, 'application/octet-stream'),
    asset_revisions.width,
    asset_revisions.height,
    asset_revisions.content_sha256,
    latest_crop.storage_path,
    latest_observation.confidence,
    face_occurrences.review_action_id,
    face_occurrences.review_action_kind,
    face_occurrences.review_person_id,
    assigned_people.display_name,
    face_occurrences.suggestion_id,
    face_occurrences.suggested_person_id,
    face_occurrences.suggested_person_name,
    face_occurrences.suggestion_model_id,
    face_occurrences.suggestion_model_hash,
    face_occurrences.suggestion_rank,
    face_occurrences.suggestion_score,
    face_occurrences.suggestion_score_margin,
    face_occurrences.suggestion_status,
    face_occurrences.suggestion_generated_at_utc,
    latest_observation.bounding_box_json,
    face_occurrences.asset_revision_id
FROM candidate_faces AS face_occurrences
INNER JOIN asset_revisions
    ON asset_revisions.id = face_occurrences.asset_revision_id
INNER JOIN assets
    ON assets.id = asset_revisions.asset_id
LEFT JOIN LATERAL (
    SELECT face_crops.storage_path
    FROM face_crops
    WHERE face_crops.face_occurrence_id = face_occurrences.id
    ORDER BY face_crops.created_at_utc DESC, face_crops.id DESC
    LIMIT 1
) AS latest_crop ON TRUE
LEFT JOIN LATERAL (
    SELECT face_observations.confidence, face_observations.bounding_box_json
    FROM face_observations
    WHERE face_observations.face_occurrence_id = face_occurrences.id
    ORDER BY
        face_observations.observed_at_utc DESC,
        face_observations.detector_model_id,
        face_observations.detector_model_hash
    LIMIT 1
) AS latest_observation ON TRUE
LEFT JOIN people AS assigned_people
    ON assigned_people.id = face_occurrences.review_person_id
ORDER BY
    CASE WHEN face_occurrences.suggestion_id IS NULL THEN 1 ELSE 0 END,
    lower(face_occurrences.suggested_person_name),
    face_occurrences.suggested_person_id,
    face_occurrences.suggestion_score_margin DESC,
    face_occurrences.suggestion_score DESC,
    face_occurrences.created_at_utc DESC,
    face_occurrences.id;

\echo === needs-review-created-desc-page ===
EXPLAIN (ANALYZE, BUFFERS, WAL, TIMING FALSE, SUMMARY TRUE, FORMAT JSON)
WITH top_suggestion AS (
    SELECT
        rankings.face_occurrence_id,
        suggestions.id AS suggestion_id,
        suggestions.suggested_person_id,
        suggested_people.display_name,
        rankings.model_id,
        rankings.model_hash,
        rankings.rank,
        suggestions.score,
        rankings.score_margin,
        suggestions.status,
        rankings.generated_at_utc
    FROM identity_suggestion_rankings AS rankings
    INNER JOIN identity_suggestions AS suggestions
        ON suggestions.id = rankings.suggestion_id
    INNER JOIN people AS suggested_people
        ON suggested_people.id = suggestions.suggested_person_id
       AND suggested_people.merged_into_person_id IS NULL
    WHERE rankings.rank = 1
      AND suggestions.status = 'pending'
      AND rankings.model_id = :'gallery_model_id'
      AND rankings.model_hash = :'gallery_model_hash'
),
candidate_faces AS (
    SELECT
        face_occurrences.id,
        face_occurrences.ordinal,
        face_occurrences.created_at_utc,
        face_occurrences.asset_revision_id,
        latest_action.id AS review_action_id,
        latest_action.action_kind AS review_action_kind,
        latest_action.person_id AS review_person_id,
        top_suggestion.suggestion_id,
        top_suggestion.suggested_person_id,
        top_suggestion.display_name AS suggested_person_name,
        top_suggestion.model_id AS suggestion_model_id,
        top_suggestion.model_hash AS suggestion_model_hash,
        top_suggestion.rank AS suggestion_rank,
        top_suggestion.score AS suggestion_score,
        top_suggestion.score_margin AS suggestion_score_margin,
        top_suggestion.status AS suggestion_status,
        top_suggestion.generated_at_utc AS suggestion_generated_at_utc
    FROM face_occurrences
    LEFT JOIN LATERAL (
        SELECT review_actions.id, review_actions.action_kind, review_actions.person_id
        FROM review_actions
        WHERE review_actions.face_occurrence_id = face_occurrences.id
          AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
          AND review_actions.reversed_at_utc IS NULL
        ORDER BY review_actions.id DESC
        LIMIT 1
    ) AS latest_action ON TRUE
    LEFT JOIN top_suggestion
        ON top_suggestion.face_occurrence_id = face_occurrences.id
    WHERE latest_action.id IS NULL
    ORDER BY face_occurrences.created_at_utc DESC, face_occurrences.id
    LIMIT $Limit OFFSET 0
)
SELECT face_occurrences.id
FROM candidate_faces AS face_occurrences
INNER JOIN asset_revisions
    ON asset_revisions.id = face_occurrences.asset_revision_id
INNER JOIN assets
    ON assets.id = asset_revisions.asset_id
LEFT JOIN LATERAL (
    SELECT face_crops.storage_path
    FROM face_crops
    WHERE face_crops.face_occurrence_id = face_occurrences.id
    ORDER BY face_crops.created_at_utc DESC, face_crops.id DESC
    LIMIT 1
) AS latest_crop ON TRUE
LEFT JOIN LATERAL (
    SELECT face_observations.confidence, face_observations.bounding_box_json
    FROM face_observations
    WHERE face_observations.face_occurrence_id = face_occurrences.id
    ORDER BY
        face_observations.observed_at_utc DESC,
        face_observations.detector_model_id,
        face_observations.detector_model_hash
    LIMIT 1
) AS latest_observation ON TRUE
LEFT JOIN people AS assigned_people
    ON assigned_people.id = face_occurrences.review_person_id
ORDER BY face_occurrences.created_at_utc DESC, face_occurrences.id;

\echo === needs-review-all-count ===
EXPLAIN (ANALYZE, BUFFERS, WAL, TIMING FALSE, SUMMARY TRUE, FORMAT JSON)
SELECT COUNT(*)
FROM face_occurrences
WHERE NOT EXISTS (
    SELECT 1
    FROM review_actions
    WHERE review_actions.face_occurrence_id = face_occurrences.id
      AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
      AND review_actions.reversed_at_utc IS NULL);

\echo === needs-review-high-confidence-count ===
EXPLAIN (ANALYZE, BUFFERS, WAL, TIMING FALSE, SUMMARY TRUE, FORMAT JSON)
WITH top_suggestion AS (
    SELECT
        rankings.face_occurrence_id,
        suggestions.id AS suggestion_id,
        suggestions.suggested_person_id,
        suggestions.score,
        rankings.score_margin
    FROM identity_suggestion_rankings AS rankings
    INNER JOIN identity_suggestions AS suggestions
        ON suggestions.id = rankings.suggestion_id
    INNER JOIN people AS suggested_people
        ON suggested_people.id = suggestions.suggested_person_id
       AND suggested_people.merged_into_person_id IS NULL
    WHERE rankings.rank = 1
      AND suggestions.status = 'pending'
      AND rankings.model_id = :'gallery_model_id'
      AND rankings.model_hash = :'gallery_model_hash'
)
SELECT COUNT(*)
FROM face_occurrences
INNER JOIN top_suggestion
    ON top_suggestion.face_occurrence_id = face_occurrences.id
WHERE NOT EXISTS (
        SELECT 1
        FROM review_actions
        WHERE review_actions.face_occurrence_id = face_occurrences.id
          AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
          AND review_actions.reversed_at_utc IS NULL)
  AND top_suggestion.suggestion_id IS NOT NULL
  AND top_suggestion.score >= :gallery_high_score_threshold
  AND top_suggestion.score_margin IS NOT NULL
  AND top_suggestion.score_margin >= :gallery_high_margin_threshold;
"@

$header = @(
    "Photo Identity PostgreSQL gallery plan evidence",
    "captured-at-utc: $([DateTimeOffset]::UtcNow.ToString('O'))",
    "connection-environment-variable: $ConnectionEnvironmentVariable",
    "statement-timeout-seconds: $StatementTimeoutSeconds",
    "note: connection string and credentials are intentionally omitted",
    ""
)
Set-Content -LiteralPath $OutputPath -Value $header -Encoding UTF8

$composeArgs = @(
    "compose",
    "--env-file", $EnvironmentPath,
    "-f", $composePath,
    "exec", "-T",
    "-e", "TARGET_DATABASE=$databaseName",
    "postgres",
    "sh", "-lc",
    'PGPASSWORD="$POSTGRES_PASSWORD" psql -X -v ON_ERROR_STOP=1 -A -t -U "$POSTGRES_USER" -d "$TARGET_DATABASE"'
)

$probeOutput = $sql | & podman @composeArgs 2>&1
$exitCode = $LASTEXITCODE
$probeOutput | Add-Content -LiteralPath $OutputPath -Encoding UTF8

if ($exitCode -ne 0) {
    throw "PostgreSQL gallery plan capture failed. The partial privacy-safe report was retained at '$OutputPath'."
}

Write-Host "PostgreSQL gallery plan capture passed."
Write-Host "report: $OutputPath"
Write-Host "The report omits the connection string and credentials. Attach the report for WI-0104 query-plan review before adding indexes."
