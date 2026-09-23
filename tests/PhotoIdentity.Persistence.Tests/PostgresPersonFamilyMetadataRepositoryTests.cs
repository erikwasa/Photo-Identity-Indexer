using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPersonFamilyMetadataRepositoryTests
{
    [Fact]
    public async Task Family_metadata_preserves_precision_inverse_relationships_and_merge_semantics()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_person_family_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };

        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand createDatabase = adminConnection.CreateCommand())
        {
            createDatabase.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder testBuilder = new(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };

            await using PostgresCatalogueDatabase database = new(testBuilder.ConnectionString);
            PostgresInitializationResult initialization = await database.TryInitializeAsync();
            Assert.Null(initialization.Error);
            Assert.Equal(PostgresCatalogueDatabase.CurrentSchemaVersion, initialization.Health.SchemaVersion);

            PersonId alice = PersonId.New();
            PersonId bob = PersonId.New();
            PersonId carol = PersonId.New();
            DateTimeOffset now = new(2026, 9, 23, 20, 0, 0, TimeSpan.Zero);
            await SeedPeopleAsync(database, now, alice, "Alice", bob, "Bob", carol, "Carol");

            PostgresPersonFamilyMetadataRepository repository = new(database);
            await repository.SetBirthDateAsync(
                alice,
                new PersonBirthDate(1984, 7, null, "month"),
                "tester",
                now);

            PersonFamilyRelationship parent = await repository.AddRelationshipAsync(
                alice,
                carol,
                "parent",
                "tester",
                now);
            PersonFamilyRelationship spouse = await repository.AddRelationshipAsync(
                alice,
                bob,
                "spouse",
                "tester",
                now);

            PersonFamilyMetadata aliceMetadata = await repository.GetAsync(alice);
            Assert.Equal("month", aliceMetadata.BirthDate?.Precision);
            Assert.Equal(7, aliceMetadata.BirthDate?.Month);
            Assert.Contains(
                aliceMetadata.Relationships,
                relationship => relationship.Id == parent.Id &&
                    relationship.Kind == "parent" &&
                    relationship.RelatedPersonId == carol);
            Assert.Contains(
                aliceMetadata.Relationships,
                relationship => relationship.Id == spouse.Id &&
                    relationship.Kind == "spouse" &&
                    relationship.RelatedPersonId == bob);

            PersonFamilyMetadata carolMetadata = await repository.GetAsync(carol);
            Assert.Contains(
                carolMetadata.Relationships,
                relationship => relationship.Id == parent.Id &&
                    relationship.Kind == "child" &&
                    relationship.RelatedPersonId == alice);

            PostgresPersonMaintenanceRepository maintenance = new(database);
            await maintenance.MergeAsync(
                alice,
                bob,
                confirmIrreversible: true,
                actor: "tester",
                createdAtUtc: now.AddMinutes(1));

            PersonFamilyMetadata bobMetadata = await repository.GetAsync(bob);
            Assert.Equal(new PersonBirthDate(1984, 7, null, "month"), bobMetadata.BirthDate);
            Assert.Contains(
                bobMetadata.Relationships,
                relationship => relationship.Kind == "parent" &&
                    relationship.RelatedPersonId == carol);
            Assert.DoesNotContain(
                bobMetadata.Relationships,
                relationship => relationship.RelatedPersonId == bob);

            PersonFamilyMetadata carolAfterMerge = await repository.GetAsync(carol);
            Assert.Contains(
                carolAfterMerge.Relationships,
                relationship => relationship.Kind == "child" &&
                    relationship.RelatedPersonId == bob);
            Assert.DoesNotContain(
                carolAfterMerge.Relationships,
                relationship => relationship.RelatedPersonId == alice);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedPeopleAsync(
        PostgresCatalogueDatabase database,
        DateTimeOffset now,
        PersonId first,
        string firstName,
        PersonId second,
        string secondName,
        PersonId third,
        string thirdName)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO people (id, display_name, created_at_utc)
            VALUES
                (@first, @first_name, @now),
                (@second, @second_name, @now),
                (@third, @third_name, @now);
            """;
        command.Parameters.AddWithValue("first", NpgsqlDbType.Uuid, first.Value);
        command.Parameters.AddWithValue("first_name", NpgsqlDbType.Text, firstName);
        command.Parameters.AddWithValue("second", NpgsqlDbType.Uuid, second.Value);
        command.Parameters.AddWithValue("second_name", NpgsqlDbType.Text, secondName);
        command.Parameters.AddWithValue("third", NpgsqlDbType.Uuid, third.Value);
        command.Parameters.AddWithValue("third_name", NpgsqlDbType.Text, thirdName);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
