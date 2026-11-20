namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Durable, reversible case-only archive source identity mapping.
/// Merely adding this schema does not retire any asset or change query results.
/// Retire/apply and restore endpoints are intentionally deferred.
/// </summary>
public sealed partial class PostgresCatalogueDatabase
{
    private const string ArchiveCaseAliasRetirementSchemaMigration = """
        CREATE TABLE archive_case_alias_retirements (
            secondary_asset_id uuid NOT NULL PRIMARY KEY
                REFERENCES assets (id) ON DELETE RESTRICT,
            survivor_asset_id uuid NOT NULL
                REFERENCES assets (id) ON DELETE RESTRICT,
            source_id uuid NOT NULL
                REFERENCES sources (id) ON DELETE RESTRICT,
            secondary_source_key text NOT NULL CHECK (btrim(secondary_source_key) <> ''),
            survivor_source_key text NOT NULL CHECK (btrim(survivor_source_key) <> ''),
            expected_sha256 text NOT NULL
                CHECK (expected_sha256 ~ '^[0-9a-f]{64}$'),
            retired_at_utc timestamptz NOT NULL,
            retired_by text NOT NULL CHECK (btrim(retired_by) <> ''),
            restored_at_utc timestamptz NULL,
            restored_by text NULL,
            CONSTRAINT ck_archive_alias_retirement_distinct_assets
                CHECK (secondary_asset_id <> survivor_asset_id),
            CONSTRAINT ck_archive_alias_retirement_restore
                CHECK (
                    (restored_at_utc IS NULL AND restored_by IS NULL)
                    OR (restored_at_utc IS NOT NULL
                        AND restored_at_utc >= retired_at_utc
                        AND restored_by IS NOT NULL
                        AND btrim(restored_by) <> '')
                )
        );
        CREATE UNIQUE INDEX ux_archive_alias_one_active_secondary_per_survivor
            ON archive_case_alias_retirements (survivor_asset_id)
            WHERE restored_at_utc IS NULL;
        CREATE INDEX ix_archive_alias_active_source
            ON archive_case_alias_retirements (source_id, survivor_asset_id)
            WHERE restored_at_utc IS NULL;
        """;
}
