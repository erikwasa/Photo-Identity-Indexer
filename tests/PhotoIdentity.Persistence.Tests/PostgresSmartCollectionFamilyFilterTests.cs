using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresSmartCollectionFamilyFilterTests
{
    [Fact]
    public async Task Age_and_relationship_filters_match_current_people_and_effective_capture_dates()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_smart_family_{Guid.NewGuid():N}";
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

            PersonId alice = PersonId.New();
            PersonId carol = PersonId.New();
            AssetRevisionId family2015 = AssetRevisionId.New();
            AssetRevisionId alice2025 = AssetRevisionId.New();
            await SeedAsync(database, alice, carol, family2015, alice2025);

            PostgresPersonFamilyMetadataRepository family = new(database);
            await family.SetBirthDateAsync(
                alice,
                new PersonBirthDate(2010, null, null, "year"),
                "test",
                DateTimeOffset.UtcNow);
            await family.AddRelationshipAsync(
                alice,
                carol,
                "parent",
                "test",
                DateTimeOffset.UtcNow);
            await family.AddRelationshipAsync(
                alice,
                carol,
                "cousin",
                "test",
                DateTimeOffset.UtcNow);

            PostgresSmartCollectionRepository definitions = new(database, TimeProvider.System);
            PostgresSmartCollectionQueryRepository query = new(database, definitions, TimeProvider.System);

            // The exact-count path must retain semantics when its date/geo
            // joins are conditionally omitted or included. Paging still returns
            // the complete population total, even with limit=1.
            SmartCollectionPhotoPage unfiltered = await query.QueryAsync(
                new SmartCollectionFilter(), limit: 1);
            Assert.Equal(2, unfiltered.Total);
            Assert.Single(unfiltered.Items);

            SmartCollectionPhotoPage peopleOnly = await query.QueryAsync(
                new SmartCollectionFilter(people: [alice]), limit: 1);
            Assert.Equal(2, peopleOnly.Total);
            Assert.Single(peopleOnly.Items);

            SmartCollectionPhotoPage takenOnly = await query.QueryAsync(
                new SmartCollectionFilter(taken: SmartCollectionDateRange.Parse("2015")));
            Assert.Equal(1, takenOnly.Total);
            Assert.Equal(family2015, Assert.Single(takenOnly.Items).RevisionId);

            SmartCollectionGeoBounds location = new(40, 10, 44, 15);
            SmartCollectionPhotoPage locationOnly = await query.QueryAsync(
                new SmartCollectionFilter(location: location));
            Assert.Equal(1, locationOnly.Total);
            Assert.Equal(family2015, Assert.Single(locationOnly.Items).RevisionId);

            SmartCollectionPhotoPage locationAndTaken = await query.QueryAsync(
                new SmartCollectionFilter(
                    location: location,
                    taken: SmartCollectionDateRange.Parse("2015")));
            Assert.Equal(1, locationAndTaken.Total);
            Assert.Equal(family2015, Assert.Single(locationAndTaken.Items).RevisionId);

            SmartCollectionPhotoPage age = await query.QueryAsync(
                new SmartCollectionFilter(
                    age: new SmartCollectionAgeCriterion(alice, 4, 5)));
            Assert.Equal(1, age.Total);
            Assert.Equal(family2015, Assert.Single(age.Items).RevisionId);

            SmartCollectionPhotoPage parentOf = await query.QueryAsync(
                new SmartCollectionFilter(
                    relationship: new SmartCollectionRelationshipCriterion(alice, ["parent"])));
            Assert.Equal(1, parentOf.Total);
            Assert.Equal(family2015, Assert.Single(parentOf.Items).RevisionId);

            SmartCollectionPhotoPage childOf = await query.QueryAsync(
                new SmartCollectionFilter(
                    relationship: new SmartCollectionRelationshipCriterion(carol, ["child"])));
            Assert.Equal(2, childOf.Total);
            Assert.Contains(childOf.Items, photo => photo.RevisionId == family2015);
            Assert.Contains(childOf.Items, photo => photo.RevisionId == alice2025);

            SmartCollectionPhotoPage cousinOf = await query.QueryAsync(
                new SmartCollectionFilter(
                    relationship: new SmartCollectionRelationshipCriterion(alice, ["cousin"])));
            Assert.Equal(1, cousinOf.Total);
            Assert.Equal(family2015, Assert.Single(cousinOf.Items).RevisionId);

            // Relationship + people exercises the staged page materialization
            // added for expensive slideshow covers. QueryAll uses the established
            // non-paged SQL and therefore provides an independent result baseline.
            SmartCollectionFilter familyPageFilter = new(
                people: [alice],
                relationship: new SmartCollectionRelationshipCriterion(alice, ["parent", "cousin"]));
            SmartCollectionPhotoPage familyPage = await query.QueryAsync(
                familyPageFilter, offset: 0, limit: 1);
            SmartCollectionPhoto[] familyAll = (await query.QueryAllAsync(familyPageFilter)).ToArray();
            Assert.Equal(familyAll.Length, familyPage.Total);
            Assert.Single(familyPage.Items);
            Assert.Equal(familyAll[0].RevisionId, familyPage.Items[0].RevisionId);
            Assert.Equal(
                familyAll[0].PeopleKeys?.Order(StringComparer.Ordinal),
                familyPage.Items[0].PeopleKeys?.Order(StringComparer.Ordinal));
            Assert.Equal(familyAll[0].EffectiveCaptureDate, familyPage.Items[0].EffectiveCaptureDate);

            SmartCollectionPhotoPage pastEnd = await query.QueryAsync(
                familyPageFilter, offset: 1, limit: 1);
            Assert.Empty(pastEnd.Items);
            Assert.Equal(1, pastEnd.Total);

            // Two-result relationship scope verifies ordering/tie-break and
            // nonzero offsets, not just a single slideshow cover.
            SmartCollectionFilter twoPageFilter = new(
                people: [alice],
                relationship: new SmartCollectionRelationshipCriterion(carol, ["child"]));
            SmartCollectionPhoto[] twoPageBaseline =
                (await query.QueryAllAsync(twoPageFilter)).ToArray();
            Assert.Equal(2, twoPageBaseline.Length);
            SmartCollectionPhotoPage firstPage = await query.QueryAsync(
                twoPageFilter, offset: 0, limit: 1);
            SmartCollectionPhotoPage secondPage = await query.QueryAsync(
                twoPageFilter, offset: 1, limit: 1);
            SmartCollectionPhotoPage beyond = await query.QueryAsync(
                twoPageFilter, offset: 2, limit: 1);
            Assert.Equal(2, firstPage.Total);
            Assert.Equal(2, secondPage.Total);
            Assert.Equal(2, beyond.Total);
            Assert.Equal(twoPageBaseline[0].RevisionId, Assert.Single(firstPage.Items).RevisionId);
            Assert.Equal(twoPageBaseline[1].RevisionId, Assert.Single(secondPage.Items).RevisionId);
            Assert.Empty(beyond.Items);

            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Alice with child around age five",
                new SmartCollectionFilter(
                    people: [alice],
                    age: new SmartCollectionAgeCriterion(alice, 4, 5),
                    relationship: new SmartCollectionRelationshipCriterion(alice, ["parent", "cousin"])));
            SmartCollectionDefinition reopened =
                await definitions.GetAsync(saved.Id) ?? throw new InvalidOperationException();
            Assert.Equal(alice, reopened.Filter.Age?.PersonId);
            Assert.Equal(4, reopened.Filter.Age?.MinimumYears);
            Assert.Equal(["cousin", "parent"], reopened.Filter.Relationship?.Kinds);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedAsync(
        PostgresCatalogueDatabase database,
        PersonId alice,
        PersonId carol,
        AssetRevisionId family2015,
        AssetRevisionId alice2025)
    {
        Guid source = Guid.NewGuid();
        Guid asset2015 = Guid.NewGuid();
        Guid asset2025 = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 23, 20, 0, 0, TimeSpan.Zero);

        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source, 'test', @root, @now);

            INSERT INTO people (id, display_name, created_at_utc)
            VALUES
                (@alice, 'Alice', @now),
                (@carol, 'Carol', @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES
                (@asset2015, @source, 'family-2015.jpg', @now),
                (@asset2025, @source, 'alice-2025.jpg', @now);

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
            VALUES
                (@revision2015, @asset2015, @hash2015, 123, @now, 'image/jpeg', 1000, 700),
                (@revision2025, @asset2025, @hash2025, 123, @now, 'image/jpeg', 1000, 700);

            INSERT INTO photo_person_actions (
                asset_revision_id, person_id, action_kind, actor, created_at_utc)
            VALUES
                (@revision2015, @alice, 'add', 'test', @now),
                (@revision2015, @carol, 'add', 'test', @now),
                (@revision2025, @alice, 'add', 'test', @now);

            INSERT INTO photo_capture_metadata (
                asset_revision_id, taken_at_local, utc_offset_minutes, latitude, longitude, extracted_at_utc)
            VALUES
                (@revision2015, @taken2015, NULL, 41.9028, 12.4964, @now),
                (@revision2025, @taken2025, NULL, NULL, NULL, @now);
            """;
        command.Parameters.AddWithValue("source", NpgsqlDbType.Uuid, source);
        command.Parameters.AddWithValue("root", NpgsqlDbType.Text, $"smart-family-{source:N}");
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("alice", NpgsqlDbType.Uuid, alice.Value);
        command.Parameters.AddWithValue("carol", NpgsqlDbType.Uuid, carol.Value);
        command.Parameters.AddWithValue("asset2015", NpgsqlDbType.Uuid, asset2015);
        command.Parameters.AddWithValue("asset2025", NpgsqlDbType.Uuid, asset2025);
        command.Parameters.AddWithValue("revision2015", NpgsqlDbType.Uuid, family2015.Value);
        command.Parameters.AddWithValue("revision2025", NpgsqlDbType.Uuid, alice2025.Value);
        command.Parameters.AddWithValue("hash2015", NpgsqlDbType.Text, new string('1', 64));
        command.Parameters.AddWithValue("hash2025", NpgsqlDbType.Text, new string('2', 64));
        command.Parameters.AddWithValue(
            "taken2015",
            NpgsqlDbType.Timestamp,
            new DateTime(2015, 6, 1, 12, 0, 0, DateTimeKind.Unspecified));
        command.Parameters.AddWithValue(
            "taken2025",
            NpgsqlDbType.Timestamp,
            new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Unspecified));
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
