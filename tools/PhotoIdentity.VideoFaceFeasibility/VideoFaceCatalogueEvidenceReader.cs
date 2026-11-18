using System.Buffers.Binary;
using System.Data;
using Npgsql;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Explicit, bounded snapshot reader. This does not use the production database
/// initializer, DI container or catalogue writer. Requires a separate PostgreSQL
/// SELECT-only login and a single REPEATABLE READ, READ ONLY transaction.
/// </summary>
public static class VideoFaceCatalogueEvidenceReader
{
    public const string ConnectionEnvironmentVariable =
        "PHOTOIDENTITY_WI0155_READONLY_CONNECTION_STRING";

    // Same current manual/confirmed evidence semantics as the photo identity
    // regeneration scorer, additionally restricted to current local photo
    // revisions, one alignment and one exact embedding model revision.
    internal const string EvidenceQuery = """
        WITH latest_review AS (
            SELECT face_occurrence_id, action_kind, person_id,
                   ROW_NUMBER() OVER (
                       PARTITION BY face_occurrence_id ORDER BY id DESC) AS position
            FROM public.review_actions
            WHERE action_kind IN ('assign', 'unknown', 'reject')
              AND reversed_at_utc IS NULL
        ),
        confirmed AS (
            SELECT face_occurrence_id, person_id
            FROM latest_review
            WHERE position = 1 AND action_kind = 'assign'
              AND person_id IS NOT NULL
            UNION
            SELECT label.face_occurrence_id, label.person_id
            FROM public.person_labels AS label
            WHERE label.label_kind = 'confirmed'
              AND NOT EXISTS (
                  SELECT 1 FROM public.review_actions AS action
                  WHERE action.face_occurrence_id = label.face_occurrence_id)
        ),
        eligible AS (
            SELECT confirmed.person_id, revision.content_sha256,
                   embedding.dimensions, embedding.l2_norm, embedding.vector_blob,
                   embedding.id AS embedding_id,
                   ROW_NUMBER() OVER (
                       PARTITION BY confirmed.person_id, revision.content_sha256
                       ORDER BY embedding.created_at_utc DESC, embedding.id DESC
                   ) AS unique_photo_rank
            FROM confirmed
            JOIN public.people AS person ON person.id = confirmed.person_id
                AND person.merged_into_person_id IS NULL
            JOIN public.face_occurrences AS face ON face.id = confirmed.face_occurrence_id
            JOIN public.asset_revisions AS revision ON revision.id = face.asset_revision_id
            JOIN public.assets AS asset ON asset.id = revision.asset_id
            JOIN public.face_crops AS crop ON crop.face_occurrence_id = face.id
            JOIN public.embeddings AS embedding ON embedding.face_crop_id = crop.id
            WHERE (@all_people OR confirmed.person_id = ANY(@people))
              AND asset.deleted_at_utc IS NULL
              AND asset.media_kind = 'photo'
              AND revision.media_kind = 'photo'
              AND embedding.model_id = @model_id
              AND embedding.model_hash = @model_hash
              AND crop.crop_protocol = @alignment
              AND NOT EXISTS (
                  SELECT 1 FROM public.asset_revisions AS newer
                  WHERE newer.asset_id = revision.asset_id
                    AND (newer.observed_at_utc > revision.observed_at_utc
                         OR (newer.observed_at_utc = revision.observed_at_utc
                             AND newer.id > revision.id))
              )
        ),
        distinct_photos AS (
            SELECT *, ROW_NUMBER() OVER (
                PARTITION BY person_id ORDER BY embedding_id DESC, content_sha256
            ) AS exemplar_rank
            FROM eligible WHERE unique_photo_rank = 1
        )
        SELECT person_id, content_sha256, dimensions, l2_norm, vector_blob
        FROM distinct_photos
        WHERE exemplar_rank <= @per_person_limit
        ORDER BY person_id, exemplar_rank;
        """;

    public static async Task<(
        VideoFaceCatalogueProvenance Provenance,
        VideoFaceCataloguePersonMatcher Matcher,
        IReadOnlyList<VideoFaceCataloguePrivatePersonMapEntry> PeopleMap)> LoadAsync(
        VideoFaceCatalogueCases cases, ModelDescriptor model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(model);
        string modelId = model.Id.Value;
        string modelHash = model.ModelHash.Value;
        string alignment = model.AlignmentProtocol?.Value
            ?? throw new ArgumentException("SFace requires an explicit alignment protocol.");
        if (modelId != "sface-2021dec-fp32" ||
            !string.Equals(
                modelHash,
                "0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79",
                StringComparison.Ordinal) ||
            alignment != "sface-five-point-v1")
        {
            throw new ArgumentException("Only the approved exact SFace identity and alignment may be queried.");
        }

        bool all = cases.CatalogueScope == "all-eligible" && cases.People is null;
        if (!all && (cases.CatalogueScope is not null ||
            cases.People is not { Count: >= 1 and <= VideoFaceCatalogueCasesLoader.MaximumPeople }))
        {
            throw new ArgumentException("No valid selected or all-eligible catalogue scope was supplied.");
        }

        string connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? throw new ArgumentException("A separate read-only PostgreSQL login is required.");
        NpgsqlConnectionStringBuilder builder = new(connectionString);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1") ||
            string.IsNullOrWhiteSpace(builder.Username) ||
            string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new ArgumentException("Only an explicit loopback catalogue connection is allowed.");
        }

        builder.Pooling = false;
        builder.Enlist = false;
        builder.IncludeErrorDetail = false;
        builder.ApplicationName = "wi-0155-readonly-evaluation";
        builder.CommandTimeout = 30;
        builder.Timeout = 5;
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        await using (var readonlyCommand = new NpgsqlCommand(
            "SET TRANSACTION READ ONLY;", connection, transaction))
        {
            await readonlyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var schema = new NpgsqlCommand(
            "SET LOCAL search_path TO pg_catalog, public;", connection, transaction))
        {
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        // READ ONLY on a transaction is not a substitute for a least-privilege
        // login. Fail closed if the actual session role can write ANY public table,
        // create a table, or elevate privileges. No role setup is attempted here.
        await using (var guard = new NpgsqlCommand("""
            SELECT current_setting('transaction_read_only') = 'on'
               AND NOT coalesce((SELECT rolsuper OR rolcreaterole OR rolcreatedb
                                 FROM pg_roles WHERE rolname = current_user), true)
               AND NOT has_schema_privilege(current_user, 'public', 'CREATE')
               AND NOT has_database_privilege(current_user, current_database(), 'CREATE')
               AND NOT EXISTS (
                   SELECT 1 FROM pg_class AS c
                   JOIN pg_namespace AS n ON n.oid = c.relnamespace
                   WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
                     AND (has_table_privilege(c.oid, 'INSERT')
                       OR has_table_privilege(c.oid, 'UPDATE')
                       OR has_table_privilege(c.oid, 'DELETE')
                       OR has_table_privilege(c.oid, 'TRUNCATE'))
               );
            """, connection, transaction))
        {
            if (await guard.ExecuteScalarAsync(cancellationToken) is not true)
            {
                throw new InvalidOperationException("The catalogue login is not a least-privilege read-only role.");
            }
        }

        DateTimeOffset captured;
        string snapshot;
        long lastReview;
        long lastEmbedding;
        await using (var version = new NpgsqlCommand("""
            SELECT clock_timestamp(), pg_current_snapshot()::text,
                   COALESCE((SELECT MAX(id) FROM public.review_actions), 0),
                   COALESCE((SELECT MAX(id) FROM embeddings
                       WHERE model_id = @model_id AND model_hash = @model_hash), 0);
            """, connection, transaction))
        {
            version.Parameters.AddWithValue("model_id", modelId);
            version.Parameters.AddWithValue("model_hash", modelHash);
            await using var reader = await version.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new DataException("The catalogue snapshot could not be identified.");
            }
            captured = reader.GetFieldValue<DateTimeOffset>(0);
            snapshot = reader.GetString(1);
            lastReview = reader.GetInt64(2);
            lastEmbedding = reader.GetInt64(3);
        }

        Dictionary<Guid, string> aliases;
        List<VideoFaceCataloguePrivatePersonMapEntry> peopleMap = [];
        if (all)
        {
            // All current canonical people, including those without eligible
            // matching SFace evidence, are counted. Only eligible people can
            // appear in rankings. The private map is operator-only.
            await using var people = new NpgsqlCommand("""
                SELECT id, display_name
                FROM public.people
                WHERE merged_into_person_id IS NULL
                ORDER BY id
                LIMIT @maximum_plus_one;
                """, connection, transaction);
            people.Parameters.AddWithValue("maximum_plus_one",
                VideoFaceCatalogueCasesLoader.MaximumAllPeople + 1);
            await using var reader = await people.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                int count = peopleMap.Count + 1;
                if (count > VideoFaceCatalogueCasesLoader.MaximumAllPeople)
                {
                    throw new InvalidOperationException(
                        "Entire canonical people archive exceeds the explicit evaluation safety bound.");
                }

                peopleMap.Add(new VideoFaceCataloguePrivatePersonMapEntry(
                    $"P{count:D4}", reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
            if (peopleMap.Count == 0)
            {
                throw new InvalidOperationException("No canonical people were available in the snapshot.");
            }
            aliases = peopleMap.ToDictionary(p => p.PersonId, p => p.Alias);
        }
        else
        {
            aliases = cases.People!.ToDictionary(p => p.PersonId, p => p.Alias);
        }

        var exemplars = new List<VideoFaceCatalogueExemplar>();
        await using (var command = new NpgsqlCommand(EvidenceQuery, connection, transaction))
        {
            command.Parameters.AddWithValue("all_people", all);
            command.Parameters.AddWithValue("people", all ? Array.Empty<Guid>() : aliases.Keys.ToArray());
            command.Parameters.AddWithValue("model_id", modelId);
            command.Parameters.AddWithValue("model_hash", modelHash);
            command.Parameters.AddWithValue("alignment", alignment);
            command.Parameters.AddWithValue("per_person_limit",
                VideoFaceCataloguePersonMatcher.MaximumExemplarsPerPerson);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                Guid personId = reader.GetGuid(0);
                string contentHash = reader.GetString(1);
                int dimensions = reader.GetInt32(2);
                double storedNorm = reader.GetDouble(3);
                byte[] bytes = reader.GetFieldValue<byte[]>(4);
                if (dimensions != 128 || bytes.Length != dimensions * sizeof(float) ||
                    !double.IsFinite(storedNorm) || storedNorm <= 0)
                {
                    throw new DataException("Incompatible catalogue SFace vector.");
                }

                float[] values = new float[dimensions];
                for (int i = 0; i < dimensions; i++)
                {
                    values[i] = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(
                            bytes.AsSpan(i * sizeof(float), sizeof(float))));
                }
                var vector = new EmbeddingVector(values);
                if (Math.Abs(vector.L2Norm - storedNorm) > 1e-9 * Math.Max(1, storedNorm))
                {
                    throw new DataException("Incompatible catalogue embedding norm.");
                }
                exemplars.Add(new VideoFaceCatalogueExemplar(
                    aliases[personId], contentHash, vector));
            }
        }

        // No write or transaction commit: one immutable database snapshot supplies
        // every selected clip in memory. No database connection survives inference.
        await transaction.RollbackAsync(cancellationToken);

        if (exemplars.Count == 0 ||
            (!all && aliases.Values.Except(
                exemplars.Select(e => e.PersonAlias), StringComparer.Ordinal).Any()))
        {
            throw new InvalidOperationException("No eligible exact-model exemplars for the requested catalogue scope.");
        }

        var provenance = new VideoFaceCatalogueProvenance(
            captured, snapshot, modelId, modelHash, alignment,
            aliases.Count, exemplars.Select(e => e.PersonAlias).Distinct().Count(),
            exemplars.Count, lastReview, lastEmbedding);
        return (provenance, new VideoFaceCataloguePersonMatcher(exemplars), peopleMap);
    }
}
