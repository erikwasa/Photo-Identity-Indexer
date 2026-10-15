namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// WI-0151 video catalogue foundation. This is registered as migration 33 by the
/// existing PostgreSQL schema static initializer; no video jobs or scanner are enabled.
/// </summary>
public sealed partial class PostgresCatalogueDatabase
{
    private const string VideoCatalogueSchemaMigration = """
        -- All existing assets/revisions retain photo as their media kind.
        ALTER TABLE assets
            ADD COLUMN media_kind text NOT NULL DEFAULT 'photo'
                CHECK (media_kind IN ('photo', 'video'));
        ALTER TABLE asset_revisions
            ADD COLUMN media_kind text NOT NULL DEFAULT 'photo'
                CHECK (media_kind IN ('photo', 'video'));

        ALTER TABLE assets
            ADD CONSTRAINT uq_assets_id_media_kind UNIQUE (id, media_kind);
        ALTER TABLE asset_revisions
            ADD CONSTRAINT fk_asset_revision_media_kind
            FOREIGN KEY (asset_id, media_kind)
            REFERENCES assets (id, media_kind) ON DELETE CASCADE;
        ALTER TABLE asset_revisions
            ADD CONSTRAINT uq_asset_revisions_id_media_kind UNIQUE (id, media_kind);

        CREATE INDEX ix_assets_media_kind_live
            ON assets (media_kind, source_id, id)
            WHERE deleted_at_utc IS NULL;

        CREATE TABLE video_revision_metadata (
            asset_revision_id uuid NOT NULL PRIMARY KEY,
            media_kind text NOT NULL DEFAULT 'video'
                CHECK (media_kind = 'video'),
            inspection_status text NOT NULL
                CHECK (inspection_status IN ('supported', 'unsupported')),
            unsupported_reason_code text NULL,
            container_name text NULL,
            video_codec text NULL,
            video_profile text NULL,
            audio_codec text NULL,
            pixel_format text NULL,
            color_transfer text NULL,
            coded_width integer NULL CHECK (coded_width > 0),
            coded_height integer NULL CHECK (coded_height > 0),
            display_rotation_degrees integer NULL
                CHECK (display_rotation_degrees BETWEEN -360 AND 360),
            duration_seconds numeric(16, 6) NULL
                CHECK (duration_seconds >= 0),
            average_frame_rate text NULL CHECK (
                average_frame_rate ~ '^[0-9]+/[1-9][0-9]*$'),
            capture_at_utc timestamp with time zone NULL,
            capture_time_provenance text NULL,
            camera_make text NULL,
            camera_model text NULL,
            gps_latitude double precision NULL
                CHECK (gps_latitude BETWEEN -90 AND 90),
            gps_longitude double precision NULL
                CHECK (gps_longitude BETWEEN -180 AND 180),
            inspected_at_utc timestamp with time zone NOT NULL,
            tool_sha256 text NOT NULL
                CHECK (tool_sha256 ~ '^[0-9a-f]{64}$'),
            inspection_profile_id text NOT NULL
                CHECK (btrim(inspection_profile_id) <> ''),
            CONSTRAINT ck_video_gps_coordinates_paired
                CHECK ((gps_latitude IS NULL) = (gps_longitude IS NULL)),
            CONSTRAINT ck_video_capture_provenance_paired
                CHECK ((capture_at_utc IS NULL) = (capture_time_provenance IS NULL)),
            CONSTRAINT ck_video_unsupported_reason
                CHECK (inspection_status <> 'supported'
                       OR unsupported_reason_code IS NULL),
            CONSTRAINT fk_video_revision_media_kind
                FOREIGN KEY (asset_revision_id, media_kind)
                REFERENCES asset_revisions (id, media_kind) ON DELETE CASCADE
        );

        CREATE TABLE video_still_posters (
            asset_revision_id uuid NOT NULL,
            poster_profile_id text NOT NULL
                CHECK (btrim(poster_profile_id) <> ''),
            tool_sha256 text NOT NULL
                CHECK (tool_sha256 ~ '^[0-9a-f]{64}$'),
            poster_sha256 text NOT NULL
                CHECK (poster_sha256 ~ '^[0-9a-f]{64}$'),
            poster_relative_path text NOT NULL
                CHECK (poster_relative_path LIKE 'video-posters/%'
                       AND poster_relative_path NOT LIKE '%..%'
                       AND poster_relative_path NOT LIKE '%:%'
                       AND position(chr(92) in poster_relative_path) = 0),
            encoded_bytes bigint NOT NULL CHECK (encoded_bytes > 0),
            width integer NOT NULL CHECK (width > 0),
            height integer NOT NULL CHECK (height > 0),
            generated_at_utc timestamp with time zone NOT NULL,
            PRIMARY KEY (asset_revision_id, poster_profile_id, tool_sha256),
            CONSTRAINT fk_video_still_poster_revision
                FOREIGN KEY (asset_revision_id)
                REFERENCES video_revision_metadata (asset_revision_id)
                ON DELETE CASCADE
        );
        """;
}
