namespace PhotoIdentity.Persistence.Postgres;

public sealed partial class PostgresCatalogueDatabase
{
    private const string FaceReviewSchema = """
        CREATE TABLE face_review_derivatives (
            face_occurrence_id uuid NOT NULL REFERENCES face_occurrences(id) ON DELETE CASCADE,
            profile_id text NOT NULL CHECK (btrim(profile_id) <> ''),
            encoded_byte_length bigint NOT NULL CHECK (encoded_byte_length > 0),
            content_sha256 text NOT NULL CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
            width integer NOT NULL CHECK (width > 0),
            height integer NOT NULL CHECK (height > 0),
            generated_at_utc timestamp with time zone NOT NULL,
            relative_path text NOT NULL UNIQUE CHECK (btrim(relative_path) <> ''),
            PRIMARY KEY (face_occurrence_id, profile_id)
        );
        CREATE TABLE asset_revision_face_review_completions (
            asset_revision_id uuid NOT NULL REFERENCES asset_revisions(id) ON DELETE CASCADE,
            profile_id text NOT NULL CHECK (btrim(profile_id) <> ''),
            completed_at_utc timestamp with time zone NOT NULL,
            PRIMARY KEY (asset_revision_id, profile_id)
        );
        CREATE INDEX ix_face_review_derivatives_profile ON face_review_derivatives(profile_id, face_occurrence_id);
        CREATE INDEX ix_face_review_completions_profile ON asset_revision_face_review_completions(profile_id, asset_revision_id);
        """;
}
