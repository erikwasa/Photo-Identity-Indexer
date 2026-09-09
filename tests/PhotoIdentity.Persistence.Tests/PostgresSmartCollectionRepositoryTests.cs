using Npgsql;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresSmartCollectionRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsDefinitionsAndEnforcesUniqueNames_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_smart_collections_{Guid.NewGuid():N}";
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

            PostgresSmartCollectionRepository repository = new(database, TimeProvider.System);
            PersonId firstPerson = PersonId.New();
            PersonId secondPerson = PersonId.New();

            SmartCollectionDefinition created = await repository.CreateAsync(
                "  Summer\t 2025  ",
                new SmartCollectionFilter(
                    people: [secondPerson, firstPerson],
                    peopleMatch: SmartCollectionMatchModes.Any,
                    tags: [" Trips / Italy ", " Family "],
                    tagMatch: SmartCollectionMatchModes.All,
                    location: new SmartCollectionGeoBounds(40, 10, 44, 15),
                    taken: SmartCollectionDateRange.Parse("2025/05/01-2025/05/10"),
                    locationPlace: "Sweden/Stockholm"));

            Assert.Equal("Summer 2025", created.Name);
            Assert.Equal(SmartCollectionMatchModes.Any, created.Filter.PeopleMatch);
            Assert.Equal(SmartCollectionMatchModes.All, created.Filter.TagMatch);
            Assert.Equal(["family", "trips/italy"], created.Filter.Tags);
            Assert.Equal("places/sweden/stockholm", created.Filter.LocationPlace);
            Assert.Equal(new DateOnly(2025, 5, 1), created.Filter.Taken?.From);
            Assert.Equal(new DateOnly(2025, 5, 10), created.Filter.Taken?.To);

            SmartCollectionDefinition listed = Assert.Single(await repository.ListAsync());
            Assert.Equal(created.Id, listed.Id);
            SmartCollectionDefinition? reopened = await repository.GetAsync(created.Id);
            Assert.NotNull(reopened);
            Assert.Equal(created.Filter.Tags, reopened.Filter.Tags);
            Assert.Equal(
                created.Filter.People.Select(person => person.ToString()),
                reopened.Filter.People.Select(person => person.ToString()));
            Assert.Equal(created.Filter.LocationPlace, reopened.Filter.LocationPlace);

            await Assert.ThrowsAsync<SmartCollectionNameConflictException>(() => repository.CreateAsync(
                "summer 2025",
                new SmartCollectionFilter()));

            SmartCollectionDefinition? updated = await repository.UpdateAsync(
                created.Id,
                " Italy archive ",
                new SmartCollectionFilter(
                    tags: ["Trips/Italy"],
                    tagMatch: SmartCollectionMatchModes.Any,
                    taken: SmartCollectionDateRange.Parse("2020-2021")));
            Assert.NotNull(updated);
            Assert.Equal("Italy archive", updated.Name);
            Assert.Equal(["trips/italy"], updated.Filter.Tags);
            Assert.Equal(new DateOnly(2020, 1, 1), updated.Filter.Taken?.From);
            Assert.Equal(new DateOnly(2021, 12, 31), updated.Filter.Taken?.To);
            Assert.True((created.CreatedAtUtc - updated.CreatedAtUtc).Duration() < TimeSpan.FromMilliseconds(1));
            Assert.True(updated.UpdatedAtUtc >= created.UpdatedAtUtc);

            Assert.Null(await repository.UpdateAsync(
                SmartCollectionId.New(),
                "Missing",
                new SmartCollectionFilter()));
            Assert.True(await repository.DeleteAsync(created.Id));
            Assert.False(await repository.DeleteAsync(created.Id));
            Assert.Null(await repository.GetAsync(created.Id));

            await using NpgsqlConnection verificationConnection = new(testBuilder.ConnectionString);
            await verificationConnection.OpenAsync();
            await using NpgsqlCommand countDefinitions = verificationConnection.CreateCommand();
            countDefinitions.CommandText = "SELECT COUNT(*) FROM smart_collections;";
            Assert.Equal(0L, Convert.ToInt64(await countDefinitions.ExecuteScalarAsync()));
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
