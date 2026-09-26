using System.Collections.Concurrent;
using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// PostgreSQL-backed replacement for the old file-path test catalogue. The path argument is kept
/// only so existing test setup code can migrate without changing its fixture plumbing.
/// </summary>
public sealed class PostgresTestCatalogueDatabase : ICatalogueStoreInitializer
{
    public const int CurrentSchemaVersion = PostgresCatalogueDatabase.CurrentSchemaVersion;

    private readonly PostgresTestDatabaseLease _lease;

    public PostgresTestCatalogueDatabase(string compatibilityPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compatibilityPath);
        _lease = PostgresTestDatabaseLease.Create();
        Database = new PostgresCatalogueDatabase(_lease.ConnectionString);
    }

    public PostgresCatalogueDatabase Database { get; }

    public string ConnectionString => _lease.ConnectionString;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Database.InitializeAsync(cancellationToken);

    public Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        Database.OpenConnectionAsync(cancellationToken);

    public static implicit operator PostgresCatalogueDatabase(PostgresTestCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.Database;
    }
}

/// <summary>
/// Owns an isolated PostgreSQL database for a test host or compatibility fixture.
/// Databases are dropped when their lease is disposed and any leaked leases are cleaned up when
/// the test process exits.
/// </summary>
public sealed class PostgresTestDatabaseLease : IDisposable, IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, string> ActiveDatabases = new(StringComparer.Ordinal);
    private static int _processExitHookRegistered;

    private readonly string _adminConnectionString;
    private bool _disposed;

    private PostgresTestDatabaseLease(
        string adminConnectionString,
        string databaseName,
        string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public static PostgresTestDatabaseLease Create()
    {
        string adminConnectionString = GetRequiredAdminConnectionString();
        string databaseName = $"photoidentity_test_{Guid.NewGuid():N}";
        CreateDatabase(adminConnectionString, databaseName);

        NpgsqlConnectionStringBuilder testBuilder = new(adminConnectionString)
        {
            Database = databaseName,
            Pooling = false,
        };

        ActiveDatabases[databaseName] = adminConnectionString;
        EnsureProcessExitHook();
        return new PostgresTestDatabaseLease(
            adminConnectionString,
            databaseName,
            testBuilder.ConnectionString);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ActiveDatabases.TryRemove(DatabaseName, out _);
        DropDatabase(_adminConnectionString, DatabaseName);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static string GetRequiredAdminConnectionString()
    {
        string? value = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = Environment.GetEnvironmentVariable(
            "PhotoIdentity__Postgres__ConnectionString");
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                "PostgreSQL-backed tests require PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING. " +
                "Use verify-postgres.ps1 or configure a PostgreSQL admin connection before running the suite.");
    }

    private static void CreateDatabase(string adminConnectionString, string databaseName)
    {
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };
        using NpgsqlConnection connection = new(adminBuilder.ConnectionString);
        connection.Open();
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)};";
        command.ExecuteNonQuery();
    }

    private static void DropDatabase(string adminConnectionString, string databaseName)
    {
        try
        {
            NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
            {
                Pooling = false,
            };
            using NpgsqlConnection connection = new(adminBuilder.ConnectionString);
            connection.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE);";
            command.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort process/test cleanup. The test container is disposable and an individual
            // test failure must remain the primary failure signal.
        }
    }

    private static void EnsureProcessExitHook()
    {
        if (Interlocked.Exchange(ref _processExitHookRegistered, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach ((string databaseName, string adminConnectionString) in ActiveDatabases.ToArray())
            {
                DropDatabase(adminConnectionString, databaseName);
            }
        };
    }

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
}
