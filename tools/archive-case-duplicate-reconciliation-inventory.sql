-- READ ONLY / no source originals opened. No private source keys emitted.
-- Compare the app's source-scoped verified-SHA exact duplicate semantics with
-- Windows case-insensitive archive paths. Not every same-hash pair represents
-- the same source copy: different normalized paths may be intentional.
BEGIN TRANSACTION READ ONLY;

COPY (
    WITH path_members AS (
        SELECT
            a.source_id, a.id AS asset_id, a.source_key, a.deleted_at_utc,
            lower(replace(a.source_key, chr(92), '/')) AS normalized_key,
            o.verification_state, o.verified_revision_id,
            r.content_sha256, r.size_bytes
        FROM assets AS a
        LEFT JOIN archive_source_observations AS o ON o.asset_id = a.id
        LEFT JOIN asset_revisions AS r ON r.id = o.verified_revision_id
                                      AND r.asset_id = a.id
        WHERE a.media_kind = 'photo'
    ),
    path_groups AS (
        SELECT source_id, normalized_key,
            count(*) AS copies,
            count(*) FILTER (WHERE deleted_at_utc IS NULL) AS active_copies,
            count(*) FILTER (WHERE source_key LIKE 'fideli/%') AS lower_fideli_copies,
            count(*) FILTER (WHERE source_key LIKE 'Fideli/%') AS upper_fideli_copies,
            count(*) FILTER (
                WHERE source_key LIKE 'fideli/%' AND deleted_at_utc IS NULL
            ) AS active_lower_fideli,
            count(*) FILTER (
                WHERE source_key LIKE 'Fideli/%' AND deleted_at_utc IS NULL
            ) AS active_upper_fideli,
            count(*) FILTER (
                WHERE verification_state = 'verified'
                  AND verified_revision_id IS NOT NULL
                  AND content_sha256 ~ '^[0-9a-f]{64}$'
                  AND size_bytes IS NOT NULL
            ) AS verified_count,
            count(DISTINCT content_sha256) AS distinct_hashes,
            count(DISTINCT size_bytes) AS distinct_sizes
        FROM path_members
        GROUP BY source_id, normalized_key
        HAVING count(*) >= 2
    ),
    -- Mirror PostgresExactDuplicateRepository's current verified/revision
    -- gating and active retirement exclusion, grouped PER source and hash.
    -- Marked-missing records are intentionally included if still verified
    -- (the application reports them in its exact duplicate inventory).
    verified_hash_members AS (
        SELECT a.source_id, a.id AS asset_id, a.source_key,
               lower(replace(a.source_key, chr(92), '/')) AS normalized_key,
               revision.content_sha256
        FROM assets AS a
        JOIN archive_source_observations AS observation
          ON observation.asset_id = a.id
        JOIN asset_revisions AS revision
          ON revision.id = observation.verified_revision_id
         AND revision.asset_id = a.id
        WHERE observation.verification_state = 'verified'
          AND observation.verified_revision_id IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM archive_case_alias_retirements AS retired
              WHERE retired.secondary_asset_id = a.id
                AND retired.restored_at_utc IS NULL
          )
    ),
    hash_groups AS (
        SELECT source_id, content_sha256, count(*) AS copies,
               count(DISTINCT normalized_key) AS different_normalized_paths
        FROM verified_hash_members
        GROUP BY source_id, content_sha256
        HAVING count(*) >= 2
    )
    SELECT
        (SELECT count(*) FROM path_groups)
            AS historical_casefold_collision_groups,
        (SELECT coalesce(sum(copies), 0) FROM path_groups)
            AS historical_casefold_collision_copies,
        (SELECT count(*) FROM path_groups WHERE normalized_key LIKE 'fideli/%')
            AS historical_fideli_casefold_groups,
        (SELECT count(*) FROM path_groups
         WHERE normalized_key LIKE 'fideli/%'
           AND copies = 2
           AND lower_fideli_copies = 1
           AND upper_fideli_copies = 1)
            AS fideli_exact_two_spellings_groups,
        (SELECT count(*) FROM path_groups
         WHERE normalized_key LIKE 'fideli/%'
           AND copies = 2 AND verified_count = 2
           AND distinct_hashes = 1 AND distinct_sizes = 1)
            AS fideli_verified_matching_hash_size_pairs,
        (SELECT count(*) FROM path_groups
         WHERE normalized_key LIKE 'fideli/%' AND active_copies > 1)
            AS fideli_casefold_groups_with_multiple_active_assets,
        (SELECT count(*) FROM path_groups
         WHERE normalized_key LIKE 'fideli/%' AND active_copies = 1)
            AS fideli_casefold_groups_with_one_active_asset,
        (SELECT count(*) FROM path_groups
         WHERE normalized_key LIKE 'fideli/%' AND active_copies = 0)
            AS fideli_casefold_groups_with_no_active_asset,
        (SELECT count(*) FROM path_groups
         WHERE normalized_key LIKE 'fideli/%' AND copies > 2)
            AS fideli_casefold_groups_with_three_or_more_assets,
        (SELECT count(*) FROM path_groups
         WHERE normalized_key LIKE 'fideli/%'
           AND active_lower_fideli > 0 AND active_upper_fideli > 0)
            AS fideli_both_spelling_variants_still_active,
        (SELECT count(*) FROM hash_groups)
            AS app_equivalent_verified_hash_duplicate_groups,
        (SELECT coalesce(sum(copies), 0) FROM hash_groups)
            AS app_equivalent_verified_hash_duplicate_copies,
        (SELECT count(*) FROM hash_groups
         WHERE different_normalized_paths > 1)
            AS verified_hash_duplicate_groups_spanning_distinct_paths,
        (SELECT count(*) FROM archive_case_alias_retirements
         WHERE restored_at_utc IS NULL)
            AS active_casefold_retirement_mappings,
        0 AS original_files_opened_by_this_audit,
        0 AS database_rows_changed_by_this_audit
) TO STDOUT WITH (FORMAT csv, HEADER true);

COMMIT;
