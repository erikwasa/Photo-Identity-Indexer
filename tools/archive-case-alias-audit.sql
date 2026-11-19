-- Production-safe, read-only archive case-alias inventory.
-- Requires a psql client. Writes no catalogue tables and does not hydrate files.
-- Run with psql -X -q -v ON_ERROR_STOP=1 -d photoidentity_main -f tools/archive-case-alias-audit.sql
-- Redirect stdout to a LOCAL PRIVATE CSV; do not commit the result.
--
-- Annotation counts represent retained actions/history across ALL revisions of
-- each asset, not necessarily its latest effective tag/person set. Never make
-- automated purge decisions from counts alone.
BEGIN TRANSACTION READ ONLY;

COPY (
    WITH collision_locators AS (
        SELECT
            source_id,
            lower(replace(source_key, chr(92), '/')) AS normalized_source_key,
            count(*) AS variants
        FROM assets
        WHERE media_kind = 'photo'
        GROUP BY source_id, lower(replace(source_key, chr(92), '/'))
        HAVING count(*) > 1
    ),
    candidates AS (
        SELECT
            a.source_id,
            c.normalized_source_key,
            c.variants,
            a.id AS asset_id,
            a.source_key,
            a.created_at_utc AS asset_created_at_utc,
            a.deleted_at_utc,
            o.verification_state,
            o.verified_revision_id,
            r.content_sha256 AS verified_content_sha256,
            COALESCE(annotations.tag_actions, 0) AS tag_actions,
            COALESCE(annotations.person_actions, 0) AS person_actions,
            COALESCE(annotations.manual_place_actions, 0) AS manual_place_actions,
            COALESCE(annotations.face_review_actions, 0) AS face_review_actions,
            COALESCE(annotations.person_labels, 0) AS person_labels,
            COALESCE(annotations.face_occurrences, 0) AS face_occurrences
        FROM collision_locators c
        JOIN assets a
          ON a.source_id = c.source_id
         AND lower(replace(a.source_key, chr(92), '/')) = c.normalized_source_key
         AND a.media_kind = 'photo'
        LEFT JOIN archive_source_observations o ON o.asset_id = a.id
        LEFT JOIN asset_revisions r
          ON r.id = o.verified_revision_id AND r.asset_id = a.id
        LEFT JOIN LATERAL (
            SELECT
                (SELECT count(*)
                 FROM photo_tag_actions t JOIN asset_revisions v
                   ON v.id = t.asset_revision_id
                 WHERE v.asset_id = a.id) AS tag_actions,
                (SELECT count(*)
                 FROM photo_person_actions p JOIN asset_revisions v
                   ON v.id = p.asset_revision_id
                 WHERE v.asset_id = a.id) AS person_actions,
                (SELECT count(*)
                 FROM photo_place_actions p JOIN asset_revisions v
                   ON v.id = p.asset_revision_id
                 WHERE v.asset_id = a.id AND p.source_kind = 'manual') AS manual_place_actions,
                (SELECT count(*)
                 FROM review_actions action
                 JOIN face_occurrences face ON face.id = action.face_occurrence_id
                 JOIN asset_revisions v ON v.id = face.asset_revision_id
                 WHERE v.asset_id = a.id) AS face_review_actions,
                (SELECT count(*)
                 FROM person_labels label
                 JOIN face_occurrences face ON face.id = label.face_occurrence_id
                 JOIN asset_revisions v ON v.id = face.asset_revision_id
                 WHERE v.asset_id = a.id) AS person_labels,
                (SELECT count(*)
                 FROM face_occurrences face
                 JOIN asset_revisions v ON v.id = face.asset_revision_id
                 WHERE v.asset_id = a.id) AS face_occurrences
        ) annotations ON true
    ),
    classified AS (
        SELECT
            *,
            (
                tag_actions + person_actions + manual_place_actions
                + face_review_actions + person_labels
            ) AS manual_history_actions,
            count(*) FILTER (
                WHERE tag_actions + person_actions + manual_place_actions
                    + face_review_actions + person_labels > 0
            ) OVER (
                PARTITION BY source_id, normalized_source_key
            ) AS members_with_manual_history,
            count(DISTINCT verified_content_sha256) OVER (
                PARTITION BY source_id, normalized_source_key
            ) AS distinct_verified_hashes
        FROM candidates
    )
    SELECT
        source_id,
        normalized_source_key,
        variants,
        asset_id,
        source_key,
        asset_created_at_utc,
        deleted_at_utc IS NOT NULL AS marked_missing,
        verification_state,
        verified_revision_id,
        verified_content_sha256,
        tag_actions,
        person_actions,
        manual_place_actions,
        face_review_actions,
        person_labels,
        face_occurrences,
        manual_history_actions,
        members_with_manual_history,
        CASE
            WHEN variants <> 2 THEN 'manual-review-multiple-aliases'
            WHEN members_with_manual_history > 1 THEN 'manual-review-both-annotated'
            WHEN members_with_manual_history = 1 THEN 'review-single-annotated-candidate'
            ELSE 'manual-review-no-annotation-preference'
        END AS review_classification
    FROM classified
    ORDER BY source_id, normalized_source_key, asset_created_at_utc, asset_id
) TO STDOUT WITH (FORMAT csv, HEADER true);

COMMIT;
