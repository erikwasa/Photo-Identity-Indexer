-- Production-safe, READ ONLY post-check for reversible case-only alias
-- retirement. Returns only aggregates (no private source paths / people).
-- Nothing is retired when this file is executed.
-- Invariant checks apply to currently active mappings only; restored mappings
-- are history and are NOT counted as active.
BEGIN TRANSACTION READ ONLY;

COPY (
    WITH mapped AS (
        SELECT
            retirement.secondary_asset_id,
            retirement.survivor_asset_id,
            retirement.source_id,
            survivor.deleted_at_utc IS NULL AS survivor_active,
            secondary.deleted_at_utc IS NOT NULL AS secondary_hidden,
            survivor.source_id = retirement.source_id
                AND secondary.source_id = retirement.source_id
                AS source_ids_match,
            survivor.media_kind = 'photo'
                AND secondary.media_kind = 'photo' AS both_are_photos,
            survivor.source_key = retirement.survivor_source_key
                AND secondary.source_key = retirement.secondary_source_key
                AS exact_paths_match,
            lower(replace(survivor.source_key, chr(92), '/'))
                = lower(replace(secondary.source_key, chr(92), '/'))
                AS windows_paths_match,
            survivor.created_at_utc < secondary.created_at_utc
                AS survivor_older,
            (SELECT count(*)
             FROM asset_revisions AS r
             WHERE r.asset_id = secondary.id) AS preserved_secondary_revisions,
            (SELECT count(*)
             FROM review_actions AS act
             JOIN face_occurrences AS face
               ON face.id = act.face_occurrence_id
             JOIN asset_revisions AS rev
               ON rev.id = face.asset_revision_id
             WHERE rev.asset_id = secondary.id) AS preserved_secondary_face_reviews
        FROM archive_case_alias_retirements AS retirement
        JOIN assets AS secondary
          ON secondary.id = retirement.secondary_asset_id
        JOIN assets AS survivor
          ON survivor.id = retirement.survivor_asset_id
        WHERE retirement.restored_at_utc IS NULL
    )
    SELECT
        count(*) AS active_retirement_mappings,
        count(*) FILTER (WHERE NOT survivor_active) AS missing_survivors,
        count(*) FILTER (WHERE NOT secondary_hidden) AS visible_secondaries,
        count(*) FILTER (WHERE NOT source_ids_match) AS source_id_conflicts,
        count(*) FILTER (WHERE NOT both_are_photos) AS media_kind_conflicts,
        count(*) FILTER (WHERE NOT exact_paths_match) AS changed_stored_source_keys,
        count(*) FILTER (WHERE NOT windows_paths_match) AS path_identity_conflicts,
        count(*) FILTER (WHERE NOT survivor_older) AS incorrect_survivor_order,
        coalesce(sum(preserved_secondary_revisions), 0) AS retained_secondary_revisions,
        coalesce(sum(preserved_secondary_face_reviews), 0) AS retained_secondary_face_review_actions,
        0 AS originals_deleted_by_this_check,
        0 AS database_rows_deleted_by_this_check
    FROM mapped
) TO STDOUT WITH (FORMAT csv, HEADER true);

COMMIT;
