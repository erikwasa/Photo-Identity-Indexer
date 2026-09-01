using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Owns the PostgreSQL connection pool and versioned migration bootstrap while
/// PostgreSQL is introduced alongside the still-authoritative SQLite catalogue.
/// </summary>
public sealed class PostgresCatalogueDatabase : IAsyncDisposable
{
    public const int CurrentSchemaVersion = 1;

    private const long MigrationAdvisoryLockKey = 504091701;

    private static readonly Migration[] Migrations =
    [
        new(
            1,
            "postgres-runtime-foundation",
            """
            SELECT 1;
            """),
    ];

    private readonly NpgsqlDataSource _dataSource;

    public PostgresCatalogueDatabase(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        NpgsqlDataSourceBuilder builder = new(connectionString);
        builder.ConnectionStringBuilder.ApplicationName = "PhotoIdentity";
        _dataSource = builder.Build();
    }

    public async Task<PostgresInitializationResult> TryInitializeAsync(
        CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection;
        try
        {
            connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsAuthenticationFailure(exception))
        {
            return new(
                PostgresCatalogueHealth.AuthenticationFailed,
                exception);
        }
        catch (Exception exception) when (IsConnectionUnavailable(exception))
        {
            return new(
                PostgresCatalogueHealth.Unavailable,
                exception);
        }

        await using (connection)
        {
            try
            {
                int schemaVersion = await InitializeAsync(connection, cancellationToken);
                return new(
                    PostgresCatalogueHealth.Ready(schemaVersion),
                    null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new(
                    PostgresCatalogueHealth.MigrationFailed,
                    exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
    }

    private static async Task<int> InitializeAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand migrationLock = connection.CreateCommand())
        {
            migrationLock.Transaction = transaction;
            migrationLock.CommandText =
                "SELECT pg_advisory_xact_lock(@migration_lock_key);";
            migrationLock.Parameters.AddWithValue(
                "migration_lock_key",
                MigrationAdvisoryLockKey);
            await migrationLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand ensureHistory = connection.CreateCommand())
        {
            ensureHistory.Transaction = transaction;
            ensureHistory.CommandText =
                """
                CREATE TABLE IF NOT EXISTS photo_identity_schema_migrations (
                    version integer NOT NULL PRIMARY KEY,
                    name text NOT NULL,
                    applied_at_utc timestamp with time zone NOT NULL
                );
                """;
            await ensureHistory.ExecuteNonQueryAsync(cancellationToken);
        }

        HashSet<int> appliedVersions = [];
        await using (NpgsqlCommand readHistory = connection.CreateCommand())
        {
            readHistory.Transaction = transaction;
            readHistory.CommandText =
                """
                SELECT version
                FROM photo_identity_schema_migrations
                ORDER BY version;
                """;

            await using NpgsqlDataReader reader =
                await readHistory.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                int version = reader.GetInt32(0);
                if (version > CurrentSchemaVersion)
                {
                    throw new InvalidOperationException(
                        $"PostgreSQL catalogue schema version {version} is newer than supported version {CurrentSchemaVersion}.");
                }

                appliedVersions.Add(version);
            }
        }

        foreach (Migration migration in Migrations)
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            await using (NpgsqlCommand applyMigration = connection.CreateCommand())
            {
                applyMigration.Transaction = transaction;
                applyMigration.CommandText = migration.Sql;
                await applyMigration.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (NpgsqlCommand recordMigration = connection.CreateCommand())
            {
                recordMigration.Transaction = transaction;
                recordMigration.CommandText =
                    """
                    INSERT INTO photo_identity_schema_migrations (
                        version,
                        name,
                        applied_at_utc)
                    VALUES (
                        @version,
                        @name,
                        @applied_at_utc);
                    """;
                recordMigration.Parameters.AddWithValue(
                    "version",
                    migration.Version);
                recordMigration.Parameters.AddWithValue(
                    "name",
                    migration.Name);
                recordMigration.Parameters.AddWithValue(
                    "applied_at_utc",
                    DateTimeOffset.UtcNow);
                await recordMigration.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return CurrentSchemaVersion;
    }

    private static bool IsAuthenticationFailure(PostgresException exception) =>
        exception.SqlState.StartsWith("28", StringComparison.Ordinal);

    private static bool IsConnectionUnavailable(Exception exception) =>
        exception is NpgsqlException or TimeoutException;

    private sealed record Migration(int Version, string Name, string Sql);
}
