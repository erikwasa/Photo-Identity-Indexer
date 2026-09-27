using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// Test-only ADO.NET compatibility facade for mature fixtures whose SQL still uses SQLite's
/// <c>$name</c> named-parameter spelling. The underlying provider is always Npgsql; named
/// parameters and unambiguous legacy scalar encodings are normalized for PostgreSQL. Provider-
/// specific SQL such as PRAGMA is intentionally not rewritten so obsolete SQLite-only coverage
/// remains visible and can be retired explicitly.
/// </summary>
public sealed class PostgresCompatibilityConnection : IDisposable, IAsyncDisposable
{
    private readonly NpgsqlConnection _inner;

    /// <summary>
    /// Transitional source-compatible constructor for mature fixtures that still create a
    /// connection from a historical <c>Data Source=...</c> SQLite-style compatibility key.
    /// The key is mapped to the same isolated PostgreSQL catalogue used by
    /// <see cref="PostgresTestCatalogueDatabase"/>; no SQLite provider is opened.
    /// </summary>
    public PostgresCompatibilityConnection(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _inner = new NpgsqlConnection(ResolveCompatibilityConnectionString(connectionString));
    }

    internal PostgresCompatibilityConnection(NpgsqlConnection inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public string ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value;
    }

    public ConnectionState State => _inner.State;

    public PostgresCompatibilityCommand CreateCommand() => new(_inner.CreateCommand());

    public void Open() => _inner.Open();

    public Task OpenAsync(CancellationToken cancellationToken = default) =>
        _inner.OpenAsync(cancellationToken);

    public void Close() => _inner.Close();

    public NpgsqlTransaction BeginTransaction() => _inner.BeginTransaction();

    public ValueTask<NpgsqlTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default) =>
        _inner.BeginTransactionAsync(cancellationToken);

    public ValueTask<NpgsqlTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default) =>
        _inner.BeginTransactionAsync(isolationLevel, cancellationToken);

    public void Dispose() => _inner.Dispose();

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    public static void ClearAllPools() => NpgsqlConnection.ClearAllPools();

    public static implicit operator NpgsqlConnection(PostgresCompatibilityConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection._inner;
    }

    private static string ResolveCompatibilityConnectionString(string connectionString)
    {
        const string dataSourcePrefix = "Data Source=";
        if (!connectionString.StartsWith(dataSourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        string compatibilityPath = connectionString[dataSourcePrefix.Length..];
        int optionSeparator = compatibilityPath.IndexOf(';');
        if (optionSeparator >= 0)
        {
            compatibilityPath = compatibilityPath[..optionSeparator];
        }

        compatibilityPath = compatibilityPath.Trim();
        if (compatibilityPath.Length == 0)
        {
            throw new ArgumentException(
                "The historical Data Source compatibility key must include a path.",
                nameof(connectionString));
        }

        return PostgresTestCatalogueDatabase.GetCompatibilityConnectionString(compatibilityPath);
    }
}

public sealed class PostgresCompatibilityCommand : IDisposable, IAsyncDisposable
{
    private static readonly Regex LegacyNamedParameter = new(
        @"\$(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> LegacyJsonParameterNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "box",
        "bounding_box",
        "bounding_box_json",
        "configuration",
        "configuration_json",
        "json",
        "landmarks",
        "landmarks_json",
        "metadata_json",
        "raw_metadata_json",
    };

    private readonly NpgsqlCommand _inner;
    private string _commandText = string.Empty;

    internal PostgresCompatibilityCommand(NpgsqlCommand inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public string CommandText
    {
        get => _commandText;
        set => _commandText = value ?? string.Empty;
    }

    public int CommandTimeout
    {
        get => _inner.CommandTimeout;
        set => _inner.CommandTimeout = value;
    }

    public CommandType CommandType
    {
        get => _inner.CommandType;
        set => _inner.CommandType = value;
    }

    public NpgsqlTransaction? Transaction
    {
        get => _inner.Transaction;
        set => _inner.Transaction = value;
    }

    public NpgsqlParameterCollection Parameters => _inner.Parameters;

    public NpgsqlParameter CreateParameter() => _inner.CreateParameter();

    public void Prepare()
    {
        PrepareForExecution();
        _inner.Prepare();
    }

    public Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        PrepareForExecution();
        return _inner.PrepareAsync(cancellationToken);
    }

    public int ExecuteNonQuery()
    {
        PrepareForExecution();
        return _inner.ExecuteNonQuery();
    }

    public Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        PrepareForExecution();
        return _inner.ExecuteNonQueryAsync(cancellationToken);
    }

    public object? ExecuteScalar()
    {
        PrepareForExecution();
        return _inner.ExecuteScalar();
    }

    public Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
    {
        PrepareForExecution();
        return _inner.ExecuteScalarAsync(cancellationToken);
    }

    public NpgsqlDataReader ExecuteReader()
    {
        PrepareForExecution();
        return _inner.ExecuteReader();
    }

    public NpgsqlDataReader ExecuteReader(CommandBehavior behavior)
    {
        PrepareForExecution();
        return _inner.ExecuteReader(behavior);
    }

    public Task<NpgsqlDataReader> ExecuteReaderAsync(CancellationToken cancellationToken = default)
    {
        PrepareForExecution();
        return _inner.ExecuteReaderAsync(cancellationToken);
    }

    public Task<NpgsqlDataReader> ExecuteReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken = default)
    {
        PrepareForExecution();
        return _inner.ExecuteReaderAsync(behavior, cancellationToken);
    }

    public void Dispose() => _inner.Dispose();

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    private void PrepareForExecution()
    {
        _inner.CommandText = LegacyNamedParameter.Replace(
            _commandText,
            match => $"@{match.Groups["name"].Value}");

        foreach (NpgsqlParameter parameter in _inner.Parameters)
        {
            if (parameter.ParameterName.StartsWith('$'))
            {
                parameter.ParameterName = parameter.ParameterName[1..];
            }

            NormalizeLegacyScalar(parameter);
        }
    }

    private static void NormalizeLegacyScalar(NpgsqlParameter parameter)
    {
        // SQLite fixtures historically represented UUID, UTC timestamps, and JSON columns as
        // TEXT. Restrict normalization to canonical UUID/time shapes or explicit JSON parameter
        // names so ordinary text remains text while mature seed helpers can execute against typed
        // PostgreSQL columns.
        if (parameter.Value is not string text)
        {
            return;
        }

        if (Guid.TryParseExact(text, "D", out Guid guid))
        {
            parameter.Value = guid;
            parameter.NpgsqlDbType = NpgsqlDbType.Uuid;
            return;
        }

        if (DateTimeOffset.TryParseExact(
                text,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset timestamp) &&
            timestamp.Offset == TimeSpan.Zero)
        {
            parameter.Value = timestamp;
            parameter.NpgsqlDbType = NpgsqlDbType.TimestampTz;
            return;
        }

        if (IsLegacyJsonParameter(parameter.ParameterName, text))
        {
            parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
        }
    }

    private static bool IsLegacyJsonParameter(string parameterName, string value)
    {
        string normalizedName = parameterName.TrimStart('@', '$');
        if (!LegacyJsonParameterNames.Contains(normalizedName) &&
            !normalizedName.EndsWith("_json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
