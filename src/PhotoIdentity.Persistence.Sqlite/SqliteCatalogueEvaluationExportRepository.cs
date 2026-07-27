using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Reads human-confirmed faces and exact model outputs for deterministic model-lab exports.
/// Source locators and crop storage paths never leave this adapter.
/// </summary>
public sealed class SqliteCatalogueEvaluationExportRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteCatalogueEvaluationExportRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueEvaluationExportInput> LoadAsync(
        CatalogueEvaluationScope scope,
        ModelId detectorModelId,
        Sha256Digest detectorModelHash,
        ModelId embedderModelId,
        Sha256Digest embedderModelHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        IReadOnlyList<CatalogueEvaluationSourceRevision> revisions = scope.Kind switch
        {
            CatalogueEvaluationScopeKinds.ProcessingRun when scope.ProcessingRunId is ProcessingRunId runId =>
                await GetRunRevisionsAsync(connection, runId, cancellationToken),
            CatalogueEvaluationScopeKinds.AssetRevisions =>
                await GetExplicitRevisionsAsync(connection, scope.AssetRevisionIds, cancellationToken),
            _ => throw new ArgumentException($"Unsupported catalogue evaluation scope '{scope.Kind}'.", nameof(scope)),
        };

        if (revisions.Count == 0)
        {
            throw new ArgumentException("The selected evaluation scope contains no asset revisions.", nameof(scope));
        }

        IReadOnlyList<CatalogueEvaluationFace> faces = await GetFacesAsync(
            connection,
            revisions.Select(revision => revision.Id).ToArray(),
            detectorModelId,
            detectorModelHash,
            embedderModelId,
            embedderModelHash,
            cancellationToken);
        return new CatalogueEvaluationExportInput(scope, revisions, faces);
    }

    private static async Task<IReadOnlyList<CatalogueEvaluationSourceRevision>> GetRunRevisionsAsync(
        SqliteConnection connection,
        ProcessingRunId runId,
        CancellationToken cancellationToken)
    {
        using (SqliteCommand existenceCommand = connection.CreateCommand())
        {
            existenceCommand.CommandText = "SELECT 1 FROM processing_runs WHERE id = $run_id;";
            existenceCommand.Parameters.AddWithValue("$run_id", runId.ToString());
            if (await existenceCommand.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw new KeyNotFoundException($"Processing run {runId} was not found.");
            }
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                asset_revisions.id,
                asset_revisions.content_sha256,
                processing_jobs.started_at_utc,
                processing_jobs.completed_at_utc
            FROM processing_jobs
            INNER JOIN asset_revisions
                ON asset_revisions.id = processing_jobs.asset_revision_id
            WHERE processing_jobs.processing_run_id = $run_id
            ORDER BY asset_revisions.id;
            """;
        command.Parameters.AddWithValue("$run_id", runId.ToString());
        return await ReadRevisionsAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<CatalogueEvaluationSourceRevision>> GetExplicitRevisionsAsync(
        SqliteConnection connection,
        IReadOnlyList<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        string revisionPredicate = AddRevisionParameters(command, revisionIds);
        command.CommandText = $"""
            WITH ranked_jobs AS (
                SELECT
                    processing_jobs.asset_revision_id,
                    processing_jobs.started_at_utc,
                    processing_jobs.completed_at_utc,
                    ROW_NUMBER() OVER (
                        PARTITION BY processing_jobs.asset_revision_id
                        ORDER BY processing_jobs.completed_at_utc DESC, processing_jobs.id DESC) AS row_number
                FROM processing_jobs
                WHERE processing_jobs.status = 'succeeded'
                  AND processing_jobs.asset_revision_id IN ({revisionPredicate})
            )
            SELECT
                asset_revisions.id,
                asset_revisions.content_sha256,
                ranked_jobs.started_at_utc,
                ranked_jobs.completed_at_utc
            FROM asset_revisions
            LEFT JOIN ranked_jobs
                ON ranked_jobs.asset_revision_id = asset_revisions.id
               AND ranked_jobs.row_number = 1
            WHERE asset_revisions.id IN ({revisionPredicate})
            ORDER BY asset_revisions.id;
            """;

        IReadOnlyList<CatalogueEvaluationSourceRevision> revisions = await ReadRevisionsAsync(
            command,
            cancellationToken);
        HashSet<AssetRevisionId> found = revisions.Select(revision => revision.Id).ToHashSet();
        AssetRevisionId[] missing = revisionIds.Where(id => !found.Contains(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new KeyNotFoundException(
                $"Asset revision {missing[0]} was not found; {missing.Length} requested revision(s) are missing.");
        }

        return revisions;
    }

    private static async Task<IReadOnlyList<CatalogueEvaluationSourceRevision>> ReadRevisionsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        List<CatalogueEvaluationSourceRevision> revisions = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            revisions.Add(new CatalogueEvaluationSourceRevision(
                AssetRevisionId.From(Guid.Parse(reader.GetString(0))),
                new Sha256Digest(reader.GetString(1)),
                reader.IsDBNull(2) ? null : Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : Parse(reader.GetString(3))));
        }

        return revisions;
    }

    private static async Task<IReadOnlyList<CatalogueEvaluationFace>> GetFacesAsync(
        SqliteConnection connection,
        IReadOnlyList<AssetRevisionId> revisionIds,
        ModelId detectorModelId,
        Sha256Digest detectorModelHash,
        ModelId embedderModelId,
        Sha256Digest embedderModelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        string revisionPredicate = AddRevisionParameters(command, revisionIds);
        command.CommandText = $"""
            WITH latest_action AS (
                SELECT
                    review_actions.face_occurrence_id,
                    review_actions.action_kind,
                    review_actions.person_id,
                    ROW_NUMBER() OVER (
                        PARTITION BY review_actions.face_occurrence_id
                        ORDER BY review_actions.id DESC) AS row_number
                FROM review_actions
                WHERE review_actions.action_kind IN ('assign', 'reject')
                  AND review_actions.reversed_at_utc IS NULL
            ),
            matching_embedding AS (
                SELECT
                    face_crops.face_occurrence_id,
                    embeddings.model_id,
                    embeddings.model_hash,
                    embeddings.dimensions,
                    embeddings.vector_blob,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_crops.face_occurrence_id
                        ORDER BY embeddings.created_at_utc DESC, embeddings.id DESC, face_crops.id DESC) AS row_number
                FROM face_crops
                INNER JOIN embeddings
                    ON embeddings.face_crop_id = face_crops.id
                WHERE embeddings.model_id = $embedder_model_id
                  AND embeddings.model_hash = $embedder_model_hash
            )
            SELECT
                face_occurrences.id,
                face_occurrences.asset_revision_id,
                face_occurrences.ordinal,
                latest_action.person_id,
                face_observations.detector_model_id,
                face_observations.detector_model_hash,
                matching_embedding.model_id,
                matching_embedding.model_hash,
                matching_embedding.dimensions,
                matching_embedding.vector_blob
            FROM face_occurrences
            INNER JOIN latest_action
                ON latest_action.face_occurrence_id = face_occurrences.id
               AND latest_action.row_number = 1
               AND latest_action.action_kind = 'assign'
            INNER JOIN people
                ON people.id = latest_action.person_id
               AND people.merged_into_person_id IS NULL
            INNER JOIN face_observations
                ON face_observations.face_occurrence_id = face_occurrences.id
               AND face_observations.detector_model_id = $detector_model_id
               AND face_observations.detector_model_hash = $detector_model_hash
            INNER JOIN matching_embedding
                ON matching_embedding.face_occurrence_id = face_occurrences.id
               AND matching_embedding.row_number = 1
            WHERE face_occurrences.asset_revision_id IN ({revisionPredicate})
            ORDER BY face_occurrences.asset_revision_id, face_occurrences.ordinal, face_occurrences.id;
            """;
        command.Parameters.AddWithValue("$detector_model_id", detectorModelId.ToString());
        command.Parameters.AddWithValue("$detector_model_hash", detectorModelHash.ToString());
        command.Parameters.AddWithValue("$embedder_model_id", embedderModelId.ToString());
        command.Parameters.AddWithValue("$embedder_model_hash", embedderModelHash.ToString());

        List<CatalogueEvaluationFace> faces = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int dimensions = reader.GetInt32(8);
            byte[] vectorBlob = (byte[])reader.GetValue(9);
            faces.Add(new CatalogueEvaluationFace(
                FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
                AssetRevisionId.From(Guid.Parse(reader.GetString(1))),
                reader.GetInt32(2),
                PersonId.From(Guid.Parse(reader.GetString(3))),
                new ModelId(reader.GetString(4)),
                new Sha256Digest(reader.GetString(5)),
                new ModelId(reader.GetString(6)),
                new Sha256Digest(reader.GetString(7)),
                dimensions,
                DeserializeVector(vectorBlob, dimensions)));
        }

        return faces;
    }

    private static string AddRevisionParameters(
        SqliteCommand command,
        IReadOnlyList<AssetRevisionId> revisionIds)
    {
        string[] names = new string[revisionIds.Count];
        for (int index = 0; index < revisionIds.Count; index++)
        {
            names[index] = $"$revision_{index}";
            if (!command.Parameters.Contains(names[index]))
            {
                command.Parameters.AddWithValue(names[index], revisionIds[index].ToString());
            }
        }

        return string.Join(", ", names);
    }

    private static float[] DeserializeVector(byte[] bytes, int dimensions)
    {
        if (dimensions <= 0 || bytes.Length != checked(dimensions * sizeof(float)))
        {
            throw new DataException("The stored embedding dimensions do not match its vector data.");
        }

        float[] values = new float[dimensions];
        for (int index = 0; index < dimensions; index++)
        {
            int bits = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(index * sizeof(float), sizeof(float)));
            values[index] = BitConverter.Int32BitsToSingle(bits);
        }

        _ = new EmbeddingVector(values);
        return values;
    }

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
