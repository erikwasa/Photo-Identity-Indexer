-- Read-only, privacy-sensitive comparison of CURRENT EFFECTIVE annotations.
-- Input identity scope: every pair of case-only source key aliases in assets.
-- This script does not modify PostgreSQL or access/hydrate source originals.
-- Output CSV contains private source keys and asset/revision identifiers.
--
-- Cross-revision history, person merges, manual action chronology, and unique
-- face occurrence IDs are NOT safely transferable from equality flags alone.
-- Equal effective states do NOT authorize excluding or purging either asset.
BEGIN TRANSACTION READ ONLY;

COPY (
    WITH alias_keys AS (
        SELECT
            source_id,
            lower(replace(source_key, chr(92), '/')) AS normalized_source_key
        FROM assets
        WHERE media_kind = 'photo'
        GROUP BY source_id, lower(replace(source_key, chr(92), '/'))
        HAVING count(*) = 2
    ),
    members AS (
        SELECT
            asset.source_id,
            aliases.normalized_source_key,
            asset.id AS asset_id,
            asset.source_key,
            asset.created_at_utc,
            asset.deleted_at_utc IS NOT NULL AS marked_missing,
            observation.verification_state,
            observation.verified_revision_id,
            revision.content_sha256 AS verified_hash,
            row_number() OVER (
                PARTITION BY asset.source_id, aliases.normalized_source_key
                ORDER BY asset.created_at_utc, asset.id
            ) AS side
        FROM alias_keys AS aliases
        JOIN assets AS asset
          ON asset.source_id = aliases.source_id
         AND asset.media_kind = 'photo'
         AND lower(replace(asset.source_key, chr(92), '/')) = aliases.normalized_source_key
        LEFT JOIN archive_source_observations AS observation
          ON observation.asset_id = asset.id
        LEFT JOIN asset_revisions AS revision
          ON revision.id = observation.verified_revision_id
         AND revision.asset_id = asset.id
    ),
    effective AS (
        SELECT
            member.*,
            coalesce(tags.tag_ids, ARRAY[]::bigint[]) AS effective_tag_ids,
            coalesce(people.person_ids, ARRAY[]::uuid[]) AS effective_person_ids,
            place.effective_place_tag_id,
            place.effective_place_source_kind,
            coalesce(faces.confirmed_person_ids, ARRAY[]::uuid[]) AS confirmed_face_person_ids,
            coalesce(faces.reviewed_face_count, 0) AS reviewed_face_count
        FROM members AS member
        LEFT JOIN LATERAL (
            SELECT array_agg(latest.tag_id ORDER BY latest.tag_id) AS tag_ids
            FROM (
                SELECT DISTINCT ON (action.tag_id)
                    action.tag_id, action.action_kind
                FROM photo_tag_actions AS action
                WHERE action.asset_revision_id = member.verified_revision_id
                ORDER BY action.tag_id, action.id DESC
            ) AS latest
            WHERE latest.action_kind = 'add'
        ) AS tags ON true
        LEFT JOIN LATERAL (
            SELECT array_agg(latest.person_id ORDER BY latest.person_id) AS person_ids
            FROM (
                SELECT DISTINCT ON (action.person_id)
                    action.person_id, action.action_kind
                FROM photo_person_actions AS action
                WHERE action.asset_revision_id = member.verified_revision_id
                ORDER BY action.person_id, action.id DESC
            ) AS latest
            JOIN people AS person
              ON person.id = latest.person_id
             AND person.merged_into_person_id IS NULL
            WHERE latest.action_kind = 'add'
        ) AS people ON true
        LEFT JOIN LATERAL (
            SELECT
                CASE WHEN action.action_kind = 'set' THEN action.tag_id END AS effective_place_tag_id,
                CASE WHEN action.action_kind = 'set' THEN action.source_kind END AS effective_place_source_kind
            FROM photo_place_actions AS action
            WHERE action.asset_revision_id = member.verified_revision_id
            ORDER BY action.id DESC
            LIMIT 1
        ) AS place ON true
        LEFT JOIN LATERAL (
            SELECT
                array_agg(DISTINCT canonical_person.id ORDER BY canonical_person.id)
                    FILTER (WHERE latest.action_kind = 'assign'
                            AND canonical_person.id IS NOT NULL) AS confirmed_person_ids,
                count(*) AS reviewed_face_count
            FROM (
                SELECT DISTINCT ON (face.id)
                    face.id AS face_id,
                    action.action_kind,
                    action.person_id
                FROM face_occurrences AS face
                JOIN review_actions AS action
                  ON action.face_occurrence_id = face.id
                WHERE face.asset_revision_id = member.verified_revision_id
                  AND action.reversed_at_utc IS NULL
                  AND action.action_kind IN ('assign', 'unknown', 'reject')
                ORDER BY face.id, action.id DESC
            ) AS latest
            LEFT JOIN people AS canonical_person
              ON canonical_person.id = latest.person_id
             AND canonical_person.merged_into_person_id IS NULL
        ) AS faces ON true
    )
    SELECT
        first.source_id,
        first.normalized_source_key,
        first.asset_id AS first_asset_id,
        second.asset_id AS second_asset_id,
        first.source_key AS first_source_key,
        second.source_key AS second_source_key,
        first.verified_revision_id AS first_current_revision_id,
        second.verified_revision_id AS second_current_revision_id,
        (
            first.verification_state = 'verified'
            AND second.verification_state = 'verified'
            AND first.verified_revision_id IS NOT NULL
            AND second.verified_revision_id IS NOT NULL
        ) AS both_verified,
        (first.verified_hash IS NOT DISTINCT FROM second.verified_hash) AS matching_stored_hash,
        first.marked_missing AS first_marked_missing,
        second.marked_missing AS second_marked_missing,
        cardinality(first.effective_tag_ids) AS first_effective_tags,
        cardinality(second.effective_tag_ids) AS second_effective_tags,
        (first.effective_tag_ids = second.effective_tag_ids) AS matching_effective_tags,
        cardinality(first.effective_person_ids) AS first_effective_manual_people,
        cardinality(second.effective_person_ids) AS second_effective_manual_people,
        (first.effective_person_ids = second.effective_person_ids) AS matching_effective_manual_people,
        (first.effective_place_tag_id IS NOT DISTINCT FROM second.effective_place_tag_id)
            AS matching_effective_place,
        (first.effective_place_source_kind IS NOT DISTINCT FROM second.effective_place_source_kind)
            AS matching_place_provenance,
        cardinality(first.confirmed_face_person_ids) AS first_confirmed_face_people,
        cardinality(second.confirmed_face_person_ids) AS second_confirmed_face_people,
        (first.confirmed_face_person_ids = second.confirmed_face_person_ids)
            AS matching_confirmed_face_people,
        first.reviewed_face_count AS first_reviewed_faces,
        second.reviewed_face_count AS second_reviewed_faces
    FROM effective AS first
    INNER JOIN effective AS second
      ON second.source_id = first.source_id
     AND second.normalized_source_key = first.normalized_source_key
     AND second.side = 2
    WHERE first.side = 1
    ORDER BY first.source_id, first.normalized_source_key
) TO STDOUT WITH (FORMAT csv, HEADER true);

COMMIT;
