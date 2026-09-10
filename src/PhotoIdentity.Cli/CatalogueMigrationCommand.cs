using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Cli;

internal sealed record CatalogueMigrationCommandOptions(
    string SqliteBackupPath,
    string PostgresConnectionEnvironment,
    string? ReportPath)
{
    public static CatalogueMigrationCommandOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "migrate")
        {
            throw new ArgumentException("The catalogue command requires 'migrate'.");
        }

        string? sqliteBackup = null;
        string? postgresEnvironment = null;
        string? report = null;
        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");
            switch (option)
            {
                case "--sqlite-backup":
                    sqliteBackup = Single(sqliteBackup, value, option);
                    break;
                case "--postgres-connection-env":
                    postgresEnvironment = Single(postgresEnvironment, value, option);
                    break;
                case "--report":
                    report = Single(report, value, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (sqliteBackup is null)
        {
            throw new ArgumentException("Option '--sqlite-backup' is required.");
        }

        if (postgresEnvironment is null)
        {
            throw new ArgumentException("Option '--postgres-connection-env' is required.");
        }

        return new(sqliteBackup, postgresEnvironment, report);
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Option '{option}' requires a non-empty value.")
            : value.Trim();
    }
}

internal static class CatalogueMigrationCommandRunner
{
    private static readonly HashSet<string> IgnoredSqliteTables =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "schema_migrations",
            "sqlite_sequence",
        };

    private static readonly string[] CriticalTables =
    [
        "sources",
        "assets",
        "asset_revisions",
        "face_occurrences",
        "people",
        "person_labels",
        "review_actions",
        "identity_suggestions",
        "photo_tag_actions",
        "photo_place_actions",
        "smart_collections",
        "processing_runs",
        "processing_jobs",
    ];

    public static async Task<int> RunAsync(
        CatalogueMigrationCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        string sourcePath = Path.GetFullPath(options.SqliteBackupPath);
        if (!File.Exists(sourcePath))
        {
            throw new ArgumentException("The SQLite backup file does not exist.");
        }

        string? connectionString = Environment.GetEnvironmentVariable(options.PostgresConnectionEnvironment);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("The selected PostgreSQL connection environment variable is empty or missing.");
        }

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        FileInfo sourceFile = new(sourcePath);
        string sourceSha256 = await ComputeSha256Async(sourcePath, cancellationToken);

        SqliteConnectionStringBuilder sqliteBuilder = new()
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
        };
        await using SqliteConnection source = new(sqliteBuilder.ConnectionString);
        await source.OpenAsync(cancellationToken);
        await ExecuteSqliteAsync(source, "PRAGMA query_only = ON;", cancellationToken);

        int sqliteSchemaVersion = checked((int)await ScalarInt64Async(source, "PRAGMA user_version;", cancellationToken));
        if (sqliteSchemaVersion != SqliteCatalogueDatabase.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"SQLite backup schema version {sqliteSchemaVersion} is not the supported current version {SqliteCatalogueDatabase.CurrentSchemaVersion}. " +
                "Upgrade the SQLite catalogue with the current application before taking the migration backup.");
        }

        await AssertSqliteForeignKeysAsync(source, cancellationToken);
        await AssertFreshPostgresDatabaseAsync(connectionString, cancellationToken);

        await using PostgresCatalogueDatabase database = new(connectionString);
        await database.InitializeAsync(cancellationToken);
        await using NpgsqlConnection target = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await target.BeginTransactionAsync(cancellationToken);

        Dictionary<string, SourceTable> sourceTables = await ReadSourceTablesAsync(source, cancellationToken);
        Dictionary<string, TargetTable> targetTables = await ReadTargetTablesAsync(target, transaction, cancellationToken);
        await AssertNoUnmappedSourceStateAsync(source, sourceTables, targetTables, cancellationToken);

        IReadOnlyList<string> orderedTables = await OrderByForeignKeysAsync(
            target,
            transaction,
            sourceTables.Keys.Intersect(targetTables.Keys, StringComparer.OrdinalIgnoreCase),
            cancellationToken);

        List<CatalogueMigrationTableResult> tableResults = [];
        long rowsCopied = 0;
        foreach (string tableName in orderedTables)
        {
            SourceTable sourceTable = sourceTables[tableName];
            TargetTable targetTable = targetTables[tableName];
            long sourceCount = await CountSqliteAsync(source, tableName, cancellationToken);
            await AssertColumnCompatibilityAsync(source, sourceTable, targetTable, sourceCount, cancellationToken);

            long copied = await CopyTableAsync(
                source,
                target,
                transaction,
                sourceTable,
                targetTable,
                cancellationToken);
            long targetCount = await CountPostgresAsync(target, transaction, tableName, cancellationToken);
            if (sourceCount != copied || sourceCount != targetCount)
            {
                throw new InvalidOperationException(
                    $"Table '{tableName}' count mismatch: SQLite={sourceCount}, copied={copied}, PostgreSQL={targetCount}.");
            }

            tableResults.Add(new(tableName, sourceCount, targetCount));
            rowsCopied += copied;
        }

        int sequencesRepaired = await RepairSequencesAsync(target, transaction, targetTables, cancellationToken);
        await AssertPostgresConstraintsValidatedAsync(target, transaction, cancellationToken);

        Dictionary<string, long> criticalCounts = new(StringComparer.OrdinalIgnoreCase);
        foreach (string table in CriticalTables)
        {
            if (!targetTables.ContainsKey(table))
            {
                continue;
            }

            criticalCounts[table] = await CountPostgresAsync(target, transaction, table, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        CatalogueMigrationReport report = new(
            SchemaVersion: 1,
            SourceFileName: sourceFile.Name,
            SourceSha256: sourceSha256,
            SourceBytes: sourceFile.Length,
            SqliteSchemaVersion: sqliteSchemaVersion,
            PostgresSchemaVersion: PostgresCatalogueDatabase.CurrentSchemaVersion,
            StartedAtUtc: startedAt,
            CompletedAtUtc: completedAt,
            RowsCopied: rowsCopied,
            SequencesRepaired: sequencesRepaired,
            Tables: tableResults,
            CriticalCounts: criticalCounts,
            Validation: "passed");

        if (options.ReportPath is string reportPath)
        {
            string fullReportPath = Path.GetFullPath(reportPath);
            string? directory = Path.GetDirectoryName(fullReportPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                fullReportPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            output.WriteLine($"report: {Path.GetFileName(fullReportPath)}");
        }

        output.WriteLine($"source-sha256: {sourceSha256}");
        output.WriteLine($"sqlite-schema-version: {sqliteSchemaVersion}");
        output.WriteLine($"postgres-schema-version: {PostgresCatalogueDatabase.CurrentSchemaVersion}");
        output.WriteLine($"tables-copied: {tableResults.Count}");
        output.WriteLine($"rows-copied: {rowsCopied}");
        output.WriteLine($"sequences-repaired: {sequencesRepaired}");
        output.WriteLine("validation: passed");
        return 0;
    }

    private static async Task AssertFreshPostgresDatabaseAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString) { Pooling = false };
        await using NpgsqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE';
            """;
        long tableCount = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (tableCount != 0)
        {
            throw new InvalidOperationException(
                "The PostgreSQL target is not fresh. Migrate only into a database with no existing public tables.");
        }
    }

    private static async Task<Dictionary<string, SourceTable>> ReadSourceTablesAsync(
        SqliteConnection source,
        CancellationToken cancellationToken)
    {
        Dictionary<string, SourceTable> tables = new(StringComparer.OrdinalIgnoreCase);
        await using SqliteCommand command = source.CreateCommand();
        command.CommandText =
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
            ORDER BY name;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<string> names = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            string name = reader.GetString(0);
            if (!name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)
                && !IgnoredSqliteTables.Contains(name))
            {
                names.Add(name);
            }
        }
        await reader.DisposeAsync();

        foreach (string name in names)
        {
            List<SourceColumn> columns = [];
            await using SqliteCommand info = source.CreateCommand();
            info.CommandText = $"PRAGMA table_info({QuoteSqlite(name)});";
            await using SqliteDataReader columnReader = await info.ExecuteReaderAsync(cancellationToken);
            while (await columnReader.ReadAsync(cancellationToken))
            {
                columns.Add(new(
                    columnReader.GetString(1),
                    columnReader.GetString(2),
                    Convert.ToInt32(columnReader.GetInt64(5), CultureInfo.InvariantCulture)));
            }

            tables[name] = new(name, columns);
        }

        return tables;
    }

    private static async Task<Dictionary<string, TargetTable>> ReadTargetTablesAsync(
        NpgsqlConnection target,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        Dictionary<string, List<TargetColumn>> columnsByTable = new(StringComparer.OrdinalIgnoreCase);
        await using NpgsqlCommand command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT table_name,
                   column_name,
                   data_type,
                   udt_name,
                   is_nullable,
                   column_default,
                   is_identity,
                   is_generated
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name <> 'photo_identity_schema_migrations'
            ORDER BY table_name, ordinal_position;
            """;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string table = reader.GetString(0);
            if (!columnsByTable.TryGetValue(table, out List<TargetColumn>? columns))
            {
                columns = [];
                columnsByTable[table] = columns;
            }

            columns.Add(new(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                string.Equals(reader.GetString(4), "YES", StringComparison.Ordinal),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                string.Equals(reader.GetString(6), "YES", StringComparison.Ordinal),
                !string.Equals(reader.GetString(7), "NEVER", StringComparison.Ordinal)));
        }

        return columnsByTable.ToDictionary(
            pair => pair.Key,
            pair => new TargetTable(pair.Key, pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task AssertNoUnmappedSourceStateAsync(
        SqliteConnection source,
        IReadOnlyDictionary<string, SourceTable> sourceTables,
        IReadOnlyDictionary<string, TargetTable> targetTables,
        CancellationToken cancellationToken)
    {
        foreach (string table in sourceTables.Keys.Except(targetTables.Keys, StringComparer.OrdinalIgnoreCase))
        {
            long count = await CountSqliteAsync(source, table, cancellationToken);
            if (count > 0)
            {
                throw new InvalidOperationException(
                    $"SQLite table '{table}' contains {count} row(s) but has no PostgreSQL destination table.");
            }
        }
    }

    private static async Task AssertColumnCompatibilityAsync(
        SqliteConnection source,
        SourceTable sourceTable,
        TargetTable targetTable,
        long sourceCount,
        CancellationToken cancellationToken)
    {
        HashSet<string> targetNames = targetTable.Columns.Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (SourceColumn column in sourceTable.Columns.Where(column => !targetNames.Contains(column.Name)))
        {
            await using SqliteCommand command = source.CreateCommand();
            command.CommandText =
                $"SELECT COUNT(*) FROM {QuoteSqlite(sourceTable.Name)} WHERE {QuoteSqlite(column.Name)} IS NOT NULL;";
            long populated = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (populated > 0)
            {
                throw new InvalidOperationException(
                    $"SQLite column '{sourceTable.Name}.{column.Name}' contains data but has no PostgreSQL destination column.");
            }
        }

        if (sourceCount == 0)
        {
            return;
        }

        HashSet<string> sourceNames = sourceTable.Columns.Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (TargetColumn column in targetTable.Columns)
        {
            if (sourceNames.Contains(column.Name)
                || column.Nullable
                || column.DefaultExpression is not null
                || column.Identity
                || column.Generated)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"PostgreSQL column '{targetTable.Name}.{column.Name}' is required but does not exist in SQLite.");
        }
    }

    private static async Task<long> CopyTableAsync(
        SqliteConnection source,
        NpgsqlConnection target,
        NpgsqlTransaction transaction,
        SourceTable sourceTable,
        TargetTable targetTable,
        CancellationToken cancellationToken)
    {
        HashSet<string> sourceNames = sourceTable.Columns.Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<TargetColumn> copyColumns = targetTable.Columns
            .Where(column => !column.Generated && sourceNames.Contains(column.Name))
            .ToList();
        if (copyColumns.Count == 0)
        {
            return 0;
        }

        bool deferPeopleMerge = string.Equals(targetTable.Name, "people", StringComparison.OrdinalIgnoreCase)
            && copyColumns.Any(column => string.Equals(column.Name, "merged_into_person_id", StringComparison.OrdinalIgnoreCase));
        List<TargetColumn> insertColumns = deferPeopleMerge
            ? copyColumns.Where(column => !string.Equals(column.Name, "merged_into_person_id", StringComparison.OrdinalIgnoreCase)).ToList()
            : copyColumns;

        string selectColumns = string.Join(", ", copyColumns.Select(column => QuoteSqlite(column.Name)));
        List<SourceColumn> primaryKey = sourceTable.Columns.Where(column => column.PrimaryKeyOrdinal > 0)
            .OrderBy(column => column.PrimaryKeyOrdinal)
            .ToList();
        string orderBy = primaryKey.Count == 0
            ? string.Empty
            : " ORDER BY " + string.Join(", ", primaryKey.Select(column => QuoteSqlite(column.Name)));

        await using SqliteCommand read = source.CreateCommand();
        read.CommandText = $"SELECT {selectColumns} FROM {QuoteSqlite(sourceTable.Name)}{orderBy};";
        await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken);

        await using NpgsqlCommand insert = target.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            $"INSERT INTO {QuotePostgres(targetTable.Name)} ({string.Join(", ", insertColumns.Select(column => QuotePostgres(column.Name)))}) " +
            $"VALUES ({string.Join(", ", insertColumns.Select((_, index) => $"@p{index}"))});";
        foreach ((TargetColumn column, int index) in insertColumns.Select((column, index) => (column, index)))
        {
            insert.Parameters.Add(new NpgsqlParameter($"p{index}", MapType(column)));
        }
        await insert.PrepareAsync(cancellationToken);

        List<(object Id, object MergeInto)> deferredPeopleMerges = [];
        int idOrdinal = deferPeopleMerge
            ? copyColumns.FindIndex(column => string.Equals(column.Name, "id", StringComparison.OrdinalIgnoreCase))
            : -1;
        int mergeOrdinal = deferPeopleMerge
            ? copyColumns.FindIndex(column => string.Equals(column.Name, "merged_into_person_id", StringComparison.OrdinalIgnoreCase))
            : -1;

        long copied = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            int parameterIndex = 0;
            for (int sourceIndex = 0; sourceIndex < copyColumns.Count; sourceIndex++)
            {
                TargetColumn column = copyColumns[sourceIndex];
                object value = reader.IsDBNull(sourceIndex)
                    ? DBNull.Value
                    : ConvertValue(reader.GetValue(sourceIndex), column);

                if (deferPeopleMerge && string.Equals(column.Name, "merged_into_person_id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                insert.Parameters[parameterIndex++].Value = value;
            }

            await insert.ExecuteNonQueryAsync(cancellationToken);
            copied++;

            if (deferPeopleMerge && !reader.IsDBNull(mergeOrdinal))
            {
                deferredPeopleMerges.Add((
                    ConvertValue(reader.GetValue(idOrdinal), copyColumns[idOrdinal]),
                    ConvertValue(reader.GetValue(mergeOrdinal), copyColumns[mergeOrdinal])));
            }
        }

        if (deferredPeopleMerges.Count > 0)
        {
            await using NpgsqlCommand update = target.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                $"UPDATE {QuotePostgres(targetTable.Name)} SET {QuotePostgres("merged_into_person_id")} = @merge " +
                $"WHERE {QuotePostgres("id")} = @id;";
            update.Parameters.Add(new NpgsqlParameter("merge", NpgsqlDbType.Uuid));
            update.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid));
            await update.PrepareAsync(cancellationToken);
            foreach ((object id, object mergeInto) in deferredPeopleMerges)
            {
                update.Parameters["id"].Value = id;
                update.Parameters["merge"].Value = mergeInto;
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        return copied;
    }

    private static async Task<IReadOnlyList<string>> OrderByForeignKeysAsync(
        NpgsqlConnection target,
        NpgsqlTransaction transaction,
        IEnumerable<string> candidateTables,
        CancellationToken cancellationToken)
    {
        HashSet<string> candidates = candidateTables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> dependencies = candidates.ToDictionary(
            table => table,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        await using NpgsqlCommand command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT tc.table_name, ccu.table_name AS referenced_table
            FROM information_schema.table_constraints tc
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.constraint_schema = tc.constraint_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = 'public';
            """;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string table = reader.GetString(0);
            string parent = reader.GetString(1);
            if (candidates.Contains(table)
                && candidates.Contains(parent)
                && !string.Equals(table, parent, StringComparison.OrdinalIgnoreCase))
            {
                dependencies[table].Add(parent);
            }
        }

        List<string> ordered = [];
        while (dependencies.Count > 0)
        {
            List<string> ready = dependencies
                .Where(pair => pair.Value.Count == 0)
                .Select(pair => pair.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (ready.Count == 0)
            {
                throw new InvalidOperationException(
                    "The PostgreSQL foreign-key graph contains a cross-table cycle that the offline migrator cannot order safely.");
            }

            foreach (string table in ready)
            {
                ordered.Add(table);
                dependencies.Remove(table);
                foreach (HashSet<string> remaining in dependencies.Values)
                {
                    remaining.Remove(table);
                }
            }
        }

        return ordered;
    }

    private static async Task<int> RepairSequencesAsync(
        NpgsqlConnection target,
        NpgsqlTransaction transaction,
        IReadOnlyDictionary<string, TargetTable> targetTables,
        CancellationToken cancellationToken)
    {
        int repaired = 0;
        foreach (TargetTable table in targetTables.Values.OrderBy(table => table.Name, StringComparer.Ordinal))
        {
            foreach (TargetColumn column in table.Columns.Where(column => column.Identity ||
                         (column.DefaultExpression?.Contains("nextval(", StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                await using NpgsqlCommand sequenceCommand = target.CreateCommand();
                sequenceCommand.Transaction = transaction;
                sequenceCommand.CommandText = "SELECT pg_get_serial_sequence(@table, @column);";
                sequenceCommand.Parameters.AddWithValue("table", $"public.{table.Name}");
                sequenceCommand.Parameters.AddWithValue("column", column.Name);
                string? sequence = Convert.ToString(
                    await sequenceCommand.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(sequence))
                {
                    continue;
                }

                await using NpgsqlCommand maxCommand = target.CreateCommand();
                maxCommand.Transaction = transaction;
                maxCommand.CommandText =
                    $"SELECT max({QuotePostgres(column.Name)})::bigint FROM {QuotePostgres(table.Name)};";
                object? maxResult = await maxCommand.ExecuteScalarAsync(cancellationToken);
                bool hasRows = maxResult is not null && maxResult is not DBNull;
                long value = hasRows ? Convert.ToInt64(maxResult, CultureInfo.InvariantCulture) : 1L;

                await using NpgsqlCommand setCommand = target.CreateCommand();
                setCommand.Transaction = transaction;
                setCommand.CommandText = "SELECT setval(CAST(@sequence AS regclass), @value, @is_called);";
                setCommand.Parameters.AddWithValue("sequence", sequence);
                setCommand.Parameters.AddWithValue("value", value);
                setCommand.Parameters.AddWithValue("is_called", hasRows);
                await setCommand.ExecuteNonQueryAsync(cancellationToken);
                repaired++;
            }
        }

        return repaired;
    }

    private static async Task AssertPostgresConstraintsValidatedAsync(
        NpgsqlConnection target,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM pg_constraint c
            JOIN pg_namespace n ON n.oid = c.connamespace
            WHERE n.nspname = 'public'
              AND c.contype IN ('f', 'c')
              AND NOT c.convalidated;
            """;
        long invalid = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (invalid != 0)
        {
            throw new InvalidOperationException($"PostgreSQL contains {invalid} unvalidated foreign-key/check constraint(s).");
        }
    }

    private static object ConvertValue(object value, TargetColumn column)
    {
        if (value is DBNull)
        {
            return DBNull.Value;
        }

        return MapType(column) switch
        {
            NpgsqlDbType.Uuid => value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!),
            NpgsqlDbType.Boolean => value is bool boolean ? boolean : Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
            NpgsqlDbType.Smallint => Convert.ToInt16(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Integer => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Bigint => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Real => Convert.ToSingle(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Numeric => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Bytea => value is byte[] bytes
                ? bytes
                : throw new InvalidOperationException($"Column '{column.Name}' expected binary SQLite data."),
            NpgsqlDbType.TimestampTz => ParseDateTimeOffset(value),
            NpgsqlDbType.Timestamp => ParseUnspecifiedDateTime(value),
            NpgsqlDbType.Date => DateOnly.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static DateTimeOffset ParseDateTimeOffset(object value) =>
        value is DateTimeOffset offset
            ? offset
            : value is DateTime dateTime
                ? new DateTimeOffset(dateTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                    : dateTime.ToUniversalTime())
                : DateTimeOffset.Parse(
                    Convert.ToString(value, CultureInfo.InvariantCulture)!,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);

    private static DateTime ParseUnspecifiedDateTime(object value)
    {
        DateTime parsed = value is DateTime dateTime
            ? dateTime
            : DateTime.Parse(
                Convert.ToString(value, CultureInfo.InvariantCulture)!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
        return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
    }

    private static NpgsqlDbType MapType(TargetColumn column) => column.DataType switch
    {
        "uuid" => NpgsqlDbType.Uuid,
        "boolean" => NpgsqlDbType.Boolean,
        "smallint" => NpgsqlDbType.Smallint,
        "integer" => NpgsqlDbType.Integer,
        "bigint" => NpgsqlDbType.Bigint,
        "real" => NpgsqlDbType.Real,
        "double precision" => NpgsqlDbType.Double,
        "numeric" => NpgsqlDbType.Numeric,
        "bytea" => NpgsqlDbType.Bytea,
        "json" => NpgsqlDbType.Json,
        "jsonb" => NpgsqlDbType.Jsonb,
        "timestamp with time zone" => NpgsqlDbType.TimestampTz,
        "timestamp without time zone" => NpgsqlDbType.Timestamp,
        "date" => NpgsqlDbType.Date,
        "text" or "character varying" or "character" => NpgsqlDbType.Text,
        _ => throw new InvalidOperationException(
            $"Unsupported PostgreSQL type '{column.DataType}' ({column.UdtName}) for column '{column.Name}'."),
    };

    private static async Task AssertSqliteForeignKeysAsync(SqliteConnection source, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = source.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The SQLite backup fails PRAGMA foreign_key_check and is not safe to migrate.");
        }
    }

    private static async Task ExecuteSqliteAsync(
        SqliteConnection source,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = source.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarInt64Async(
        SqliteConnection source,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = source.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<long> CountSqliteAsync(
        SqliteConnection source,
        string table,
        CancellationToken cancellationToken) =>
        await ScalarInt64Async(source, $"SELECT COUNT(*) FROM {QuoteSqlite(table)};", cancellationToken);

    private static async Task<long> CountPostgresAsync(
        NpgsqlConnection target,
        NpgsqlTransaction transaction,
        string table,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM {QuotePostgres(table)};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        using SHA256 sha = SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string QuoteSqlite(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string QuotePostgres(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private sealed record SourceTable(string Name, IReadOnlyList<SourceColumn> Columns);
    private sealed record SourceColumn(string Name, string DeclaredType, int PrimaryKeyOrdinal);
    private sealed record TargetTable(string Name, IReadOnlyList<TargetColumn> Columns);
    private sealed record TargetColumn(
        string Name,
        string DataType,
        string UdtName,
        bool Nullable,
        string? DefaultExpression,
        bool Identity,
        bool Generated);
}

internal sealed record CatalogueMigrationTableResult(
    string Table,
    long SqliteRows,
    long PostgresRows);

internal sealed record CatalogueMigrationReport(
    int SchemaVersion,
    string SourceFileName,
    string SourceSha256,
    long SourceBytes,
    int SqliteSchemaVersion,
    int PostgresSchemaVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long RowsCopied,
    int SequencesRepaired,
    IReadOnlyList<CatalogueMigrationTableResult> Tables,
    IReadOnlyDictionary<string, long> CriticalCounts,
    string Validation);
