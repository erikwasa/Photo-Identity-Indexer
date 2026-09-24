using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPersonFamilyMetadataRepository : IPersonFamilyMetadataRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresPersonFamilyMetadataRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public static async Task EnsureSchemaAsync(
        PostgresCatalogueDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = PostgresPersonFamilyMetadataSchema.Sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PersonFamilyMetadata> GetAsync(
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(_database, cancellationToken);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await RequireActivePersonAsync(connection, personId, cancellationToken);

        PersonBirthDate? birthDate = null;
        await using (NpgsqlCommand birth = connection.CreateCommand())
        {
            birth.CommandText = """
                SELECT precision, birth_year, birth_month, birth_day
                FROM person_birth_metadata
                WHERE person_id = @person_id;
                """;
            birth.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
            await using NpgsqlDataReader reader = await birth.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                birthDate = new PersonBirthDate(
                    reader.GetInt16(1),
                    reader.IsDBNull(2) ? null : reader.GetInt16(2),
                    reader.IsDBNull(3) ? null : reader.GetInt16(3),
                    reader.GetString(0));
            }
        }

        List<PersonFamilyRelationship> relationships = [];
        await using (NpgsqlCommand relationship = connection.CreateCommand())
        {
            relationship.CommandText = """
                SELECT
                    relationship.id,
                    relationship.source_person_id,
                    relationship.target_person_id,
                    relationship.relationship_kind,
                    relationship.actor,
                    relationship.created_at_utc,
                    source.display_name,
                    target.display_name
                FROM person_relationships AS relationship
                INNER JOIN people AS source ON source.id = relationship.source_person_id
                INNER JOIN people AS target ON target.id = relationship.target_person_id
                WHERE relationship.source_person_id = @person_id
                   OR relationship.target_person_id = @person_id
                ORDER BY relationship.relationship_kind, relationship.id;
                """;
            relationship.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
            await using NpgsqlDataReader reader = await relationship.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                PersonId sourceId = PersonId.From(reader.GetGuid(1));
                bool selectedIsSource = sourceId == personId;
                PersonId relatedId = PersonId.From(reader.GetGuid(selectedIsSource ? 2 : 1));
                string storedKind = reader.GetString(3);
                relationships.Add(new PersonFamilyRelationship(
                    reader.GetInt64(0),
                    personId,
                    relatedId,
                    reader.GetString(selectedIsSource ? 7 : 6),
                    selectedIsSource ? storedKind : PersonRelationshipKinds.Inverse(storedKind),
                    reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5)));
            }
        }

        return new PersonFamilyMetadata(personId, birthDate, relationships);
    }

    public async Task SetBirthDateAsync(
        PersonId personId,
        PersonBirthDate? birthDate,
        string actor,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = Required(actor, nameof(actor));
        await EnsureSchemaAsync(_database, cancellationToken);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await RequireActivePersonAsync(connection, personId, cancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        if (birthDate is null)
        {
            command.CommandText = "DELETE FROM person_birth_metadata WHERE person_id = @person_id;";
            command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
        }
        else
        {
            command.CommandText = """
                INSERT INTO person_birth_metadata (
                    person_id, precision, birth_year, birth_month, birth_day, actor, updated_at_utc)
                VALUES (
                    @person_id, @precision, @birth_year, @birth_month, @birth_day, @actor, @updated_at_utc)
                ON CONFLICT (person_id) DO UPDATE
                SET precision = EXCLUDED.precision,
                    birth_year = EXCLUDED.birth_year,
                    birth_month = EXCLUDED.birth_month,
                    birth_day = EXCLUDED.birth_day,
                    actor = EXCLUDED.actor,
                    updated_at_utc = EXCLUDED.updated_at_utc;
                """;
            command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
            command.Parameters.AddWithValue("precision", NpgsqlDbType.Text, birthDate.Precision);
            command.Parameters.AddWithValue("birth_year", NpgsqlDbType.Smallint, checked((short)birthDate.Year));
            command.Parameters.Add("birth_month", NpgsqlDbType.Smallint).Value =
                birthDate.Month is int month ? checked((short)month) : DBNull.Value;
            command.Parameters.Add("birth_day", NpgsqlDbType.Smallint).Value =
                birthDate.Day is int day ? checked((short)day) : DBNull.Value;
            command.Parameters.AddWithValue("actor", NpgsqlDbType.Text, normalizedActor);
            command.Parameters.AddWithValue("updated_at_utc", NpgsqlDbType.TimestampTz, changedAtUtc.ToUniversalTime());
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PersonFamilyRelationship> AddRelationshipAsync(
        PersonId personId,
        PersonId relatedPersonId,
        string kind,
        string actor,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (personId == relatedPersonId)
        {
            throw new ArgumentException("A person cannot have a family relationship to themselves.", nameof(relatedPersonId));
        }

        string normalizedKind = PersonRelationshipKinds.Normalize(kind);
        string normalizedActor = Required(actor, nameof(actor));
        (PersonId sourceId, PersonId targetId, string storedKind) = CanonicalStorage(personId, relatedPersonId, normalizedKind);

        await EnsureSchemaAsync(_database, cancellationToken);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await RequireActivePersonAsync(connection, personId, cancellationToken);
        await RequireActivePersonAsync(connection, relatedPersonId, cancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO person_relationships (
                source_person_id, target_person_id, relationship_kind, actor, created_at_utc)
            VALUES (@source_person_id, @target_person_id, @relationship_kind, @actor, @created_at_utc)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """;
        command.Parameters.AddWithValue("source_person_id", NpgsqlDbType.Uuid, sourceId.Value);
        command.Parameters.AddWithValue("target_person_id", NpgsqlDbType.Uuid, targetId.Value);
        command.Parameters.AddWithValue("relationship_kind", NpgsqlDbType.Text, storedKind);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Text, normalizedActor);
        command.Parameters.AddWithValue("created_at_utc", NpgsqlDbType.TimestampTz, createdAtUtc.ToUniversalTime());

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null)
        {
            PersonFamilyMetadata existing = await GetAsync(personId, cancellationToken);
            PersonFamilyRelationship? match = existing.Relationships.FirstOrDefault(item =>
                item.RelatedPersonId == relatedPersonId && item.Kind == normalizedKind);
            return match ?? throw new InvalidOperationException("The family relationship already exists but could not be resolved.");
        }

        long id = Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        PersonFamilyMetadata metadata = await GetAsync(personId, cancellationToken);
        return metadata.Relationships.Single(item => item.Id == id);
    }

    public async Task<bool> DeleteRelationshipAsync(
        PersonId personId,
        long relationshipId,
        CancellationToken cancellationToken = default)
    {
        if (relationshipId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(relationshipId));
        }

        await EnsureSchemaAsync(_database, cancellationToken);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await RequireActivePersonAsync(connection, personId, cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM person_relationships
            WHERE id = @id
              AND (source_person_id = @person_id OR target_person_id = @person_id);
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, relationshipId);
        command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static (PersonId Source, PersonId Target, string Kind) CanonicalStorage(
        PersonId personId,
        PersonId relatedPersonId,
        string kind)
    {
        if (kind == PersonRelationshipKinds.Child)
        {
            return (relatedPersonId, personId, PersonRelationshipKinds.Parent);
        }

        if (kind == PersonRelationshipKinds.Grandchild)
        {
            return (relatedPersonId, personId, PersonRelationshipKinds.Grandparent);
        }

        if (kind is PersonRelationshipKinds.Spouse or PersonRelationshipKinds.Sibling &&
            string.CompareOrdinal(personId.ToString(), relatedPersonId.ToString()) > 0)
        {
            return (relatedPersonId, personId, kind);
        }

        return (personId, relatedPersonId, kind);
    }

    private static async Task RequireActivePersonAsync(
        NpgsqlConnection connection,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM people
            WHERE id = @person_id
              AND merged_into_person_id IS NULL
              AND display_name IS NOT NULL
              AND btrim(display_name) <> '';
            """;
        command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new KeyNotFoundException($"Active person {personId} was not found.");
        }
    }

    private static string Required(string value, string parameterName)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 200)
        {
            throw new ArgumentException("The value must contain between 1 and 200 characters.", parameterName);
        }

        return normalized;
    }
}
