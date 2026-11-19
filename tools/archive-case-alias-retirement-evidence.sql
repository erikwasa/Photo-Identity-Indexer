-- Automation-focused case-only alias preflight evidence.
-- READ ONLY. Does not mutate PostgreSQL, source files, or OneDrive state.
-- Never authorize destructive action from this export alone.
-- CSV contains PRIVATE source paths and asset/revision IDs.
--
-- Actor classification uses the two canonical auto-assignment actors from
-- PostgresIdentityAutoAssignmentService. Unknown actors are human/unknown,
-- NEVER automatically considered disposable.
BEGIN TRANSACTION READ ONLY;

COPY (
    WITH pairs AS (
        SELECT
            source_id,
            lower(replace(source_key, chr(92), '/')) AS normalized_source_key
        FROM assets
        WHERE media_kind = 'photo'
        GROUP BY source_id, lower(replace(source_key, chr(92), '/'))
        HAVING count(*) = 2
    )
    SELECT
        a.source_id,
        p.normalized_source_key,
        a.id AS asset_id,
        a.source_key,
        a.created_at_utc,
        a.deleted_at_utc IS NOT NULL AS marked_missing,
        o.verification_state,
        o.verified_revision_id,
        r.content_sha256 AS verified_content_sha256,
        r.size_bytes AS verified_size_bytes,
        (
            SELECT count(*)
            FROM asset_revisions AS rev WHERE rev.asset_id = a.id
        ) AS revision_count,
        (
            SELECT count(*)
            FROM face_occurrences AS face
            JOIN asset_revisions AS rev ON rev.id = face.asset_revision_id
            WHERE rev.asset_id = a.id
        ) AS face_occurrence_count,
        (
            SELECT count(*)
            FROM review_actions AS action
            JOIN face_occurrences AS face ON face.id = action.face_occurrence_id
            JOIN asset_revisions AS rev ON rev.id = face.asset_revision_id
            WHERE rev.asset_id = a.id
              AND action.actor IN (
                  'identity-matcher:auto',
                  'identity-matcher:auto-multi-evidence')
        ) AS automatic_face_review_actions,
        (
            SELECT count(*)
            FROM review_actions AS action
            JOIN face_occurrences AS face ON face.id = action.face_occurrence_id
            JOIN asset_revisions AS rev ON rev.id = face.asset_revision_id
            WHERE rev.asset_id = a.id
              AND action.actor NOT IN (
                  'identity-matcher:auto',
                  'identity-matcher:auto-multi-evidence')
        ) AS human_or_unknown_face_review_actions,
        (
            SELECT count(*)
            FROM person_labels AS label
            JOIN face_occurrences AS face ON face.id = label.face_occurrence_id
            JOIN asset_revisions AS rev ON rev.id = face.asset_revision_id
            WHERE rev.asset_id = a.id
              AND label.assigned_by IN (
                  'identity-matcher:auto',
                  'identity-matcher:auto-multi-evidence')
        ) AS automatic_person_labels,
        (
            SELECT count(*)
            FROM person_labels AS label
            JOIN face_occurrences AS face ON face.id = label.face_occurrence_id
            JOIN asset_revisions AS rev ON rev.id = face.asset_revision_id
            WHERE rev.asset_id = a.id
              AND label.assigned_by NOT IN (
                  'identity-matcher:auto',
                  'identity-matcher:auto-multi-evidence')
        ) AS human_or_unknown_person_labels,
        (
            SELECT count(*)
            FROM photo_tag_actions AS tag
            JOIN asset_revisions AS rev ON rev.id = tag.asset_revision_id
            WHERE rev.asset_id = a.id
        ) AS photo_tag_actions,
        (
            SELECT count(*)
            FROM photo_person_actions AS person_action
            JOIN asset_revisions AS rev ON rev.id = person_action.asset_revision_id
            WHERE rev.asset_id = a.id
        ) AS photo_person_actions,
        (
            SELECT count(*)
            FROM photo_place_actions AS place_action
            JOIN asset_revisions AS rev ON rev.id = place_action.asset_revision_id
            WHERE rev.asset_id = a.id
              AND place_action.source_kind = 'manual'
        ) AS manual_place_actions,
        (
            SELECT count(*)
            FROM photo_place_actions AS place_action
            JOIN asset_revisions AS rev ON rev.id = place_action.asset_revision_id
            WHERE rev.asset_id = a.id
              AND place_action.source_kind <> 'manual'
        ) AS automatic_or_migrated_place_actions,
        (
            SELECT count(*)
            FROM photo_capture_date_actions AS capture_action
            JOIN asset_revisions AS rev ON rev.id = capture_action.asset_revision_id
            WHERE rev.asset_id = a.id
        ) AS manual_capture_date_actions,
        (
            SELECT count(*)
            FROM photo_presentation_preference_actions AS preference
            JOIN asset_revisions AS rev ON rev.id = preference.asset_revision_id
            WHERE rev.asset_id = a.id
        ) AS presentation_preference_actions,
        (
            SELECT count(*)
            FROM photo_list_collection_items AS item
            JOIN asset_revisions AS rev ON rev.id = item.asset_revision_id
            WHERE rev.asset_id = a.id
        ) AS manual_list_collection_items,
        (
            SELECT count(*)
            FROM person_featured_faces AS featured
            JOIN face_occurrences AS face ON face.id = featured.face_occurrence_id
            JOIN asset_revisions AS rev ON rev.id = face.asset_revision_id
            WHERE rev.asset_id = a.id
        ) AS featured_face_references,
        EXISTS (
            SELECT 1 FROM source_copy_exclusions AS excluded
            WHERE excluded.source_id = a.source_id
              AND lower(replace(excluded.source_key, chr(92), '/')) =
                  p.normalized_source_key
        ) AS has_source_exclusion
    FROM pairs AS p
    JOIN assets AS a
      ON a.source_id = p.source_id
     AND a.media_kind = 'photo'
     AND lower(replace(a.source_key, chr(92), '/')) = p.normalized_source_key
    LEFT JOIN archive_source_observations AS o ON o.asset_id = a.id
    LEFT JOIN asset_revisions AS r
      ON r.id = o.verified_revision_id
     AND r.asset_id = a.id
    ORDER BY a.source_id, p.normalized_source_key, a.created_at_utc, a.id
) TO STDOUT WITH (FORMAT csv, HEADER true);

COMMIT;
