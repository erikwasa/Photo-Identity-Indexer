namespace PhotoIdentity.Persistence.Postgres;

/// <summary>WI-0188 v35: independent video-only operator intent, safely off/paused.</summary>
public sealed partial class PostgresCatalogueDatabase
{
    private const string VideoProcessingControlSchemaMigration = """
        CREATE TABLE video_processing_control (
            singleton_id integer NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
            enabled boolean NOT NULL DEFAULT false,
            paused boolean NOT NULL DEFAULT true,
            generation bigint NOT NULL DEFAULT 0 CHECK (generation >= 0),
            last_action text NOT NULL DEFAULT 'initial'
                CHECK (last_action IN ('initial','pause','resume','stop')),
            updated_at_utc timestamptz NOT NULL DEFAULT now()
        );

        INSERT INTO video_processing_control(singleton_id, enabled, paused)
        VALUES (1, false, true);
        """;
}
