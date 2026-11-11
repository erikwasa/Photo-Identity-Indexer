using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class CatalogueEvidenceTests
{
    private static EmbeddingVector Vector(params float[] values) => new(values);

    [Fact]
    public void Per_person_ranking_does_not_count_second_photo_as_runner_up()
    {
        var matcher = new VideoFaceCataloguePersonMatcher(
        [
            new("P01", new string('a', 64), Vector(1, 0, 0)),
            new("P01", new string('b', 64), Vector(0.99f, 0.14f, 0)),
            new("P02", new string('c', 64), Vector(0, 1, 0)),
        ]);

        var finding = matcher.FindCandidate(1, 100, 1, Vector(1, 0, 0));
        Assert.Equal(2, matcher.ReferenceCount); // people, not exemplar count
        Assert.Equal("P01", finding.CandidateAlias);
        Assert.Equal("review-candidate", finding.ReviewStatus);
        Assert.Equal(0, finding.RunnerUpSimilarity);
        Assert.Equal("P02", matcher.FindCandidate(2, 200, 1, Vector(0, 1, 0)).CandidateAlias);
        Assert.Equal("P01", matcher.Diagnose(1, 100, 1, Vector(1, 0, 0)).TopReferenceAlias);
    }

    [Fact]
    public void Negative_and_ambiguous_cases_never_force_a_known_person()
    {
        var matcher = new VideoFaceCataloguePersonMatcher(
        [
            new("P01", new string('a', 64), Vector(1, 0, 0)),
            new("P02", new string('b', 64), Vector(0, 1, 0)),
        ]);

        var unknown = matcher.FindCandidate(1, 100, 1, Vector(0, 0, 1));
        Assert.Null(unknown.CandidateAlias);
        Assert.Equal("below-exploratory-threshold", unknown.ReviewStatus);
        Assert.Equal("P01", matcher.Diagnose(1, 100, 1, Vector(0, 0, 1)).TopReferenceAlias);

        var ambiguous = new VideoFaceCataloguePersonMatcher(
        [
            new("P01", new string('a', 64), Vector(1, 0, 0)),
            new("P02", new string('b', 64), Vector(0.99f, 0.14f, 0)),
        ]).FindCandidate(1, 100, 1, Vector(1, 0, 0));
        Assert.Equal("ambiguous", ambiguous.ReviewStatus);
    }

    [Fact]
    public void Invalid_exemplar_sets_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => new VideoFaceCataloguePersonMatcher(
        [
            new("P01", new string('a', 64), Vector(1, 0)),
            new("P01", new string('a', 64), Vector(0, 1)),
        ]));
        Assert.Throws<ArgumentException>(() => new VideoFaceCataloguePersonMatcher(
        [
            new("P01", new string('a', 64), Vector(1, 0)),
            new("P02", new string('b', 64), Vector(0, 1, 0)),
        ]));
        Assert.Throws<ArgumentException>(() => new VideoFaceCataloguePersonMatcher(
            Enumerable.Range(0, 4)
                .Select(i => new VideoFaceCatalogueExemplar(
                    "P01", i.ToString("x64"), Vector(1, 0))).ToArray()));
    }

    [Fact]
    public void Full_archive_top_three_returns_distinct_people_not_three_photos()
    {
        // More than the old 32-person selected-scope ceiling; no DB or models.
        VideoFaceCatalogueExemplar[] examples = Enumerable.Range(1, 256)
            .Select(i => new VideoFaceCatalogueExemplar(
                $"P{i:D4}", i.ToString("x64"),
                i == 255 ? Vector(1, 0, 0) : Vector(0, 1, 0)))
            .ToArray();
        var matcher = new VideoFaceCataloguePersonMatcher(examples);
        Assert.Equal(256, matcher.ReferenceCount);

        var ranks = matcher.RankTopPeople(Vector(1, 0, 0));
        Assert.Equal(new[] { "P0255", "P0001", "P0002" },
            ranks.Select(r => r.Alias).ToArray());
        Assert.Equal(3, ranks.Select(r => r.Alias).Distinct().Count());
        Assert.Equal(1, ranks[0].Score);
        Assert.Equal(0, ranks[1].Score);

        var diagnostic = matcher.Diagnose(1, 100, 1, Vector(1, 0, 0));
        Assert.Equal(3, diagnostic.TopCandidates!.Count);
        Assert.Equal(ranks, diagnostic.TopCandidates);

        var onePass = matcher.Evaluate(1, 100, 1, Vector(1, 0, 0));
        Assert.Equal("P0255", onePass.Finding.CandidateAlias);
        Assert.Equal("P0255", onePass.Diagnostic.TopCandidates![0].Alias);
        Assert.DoesNotContain("PersonId", JsonSerializer.Serialize(onePass),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Group_ranking_combines_two_representatives_and_preserves_singletons()
    {
        var matcher = new VideoFaceCataloguePersonMatcher(
        [
            new("P01", new string('a', 64), Vector(1, 0, 0)),
            new("P01", new string('b', 64), Vector(.99f, .14f, 0)),
            new("P02", new string('c', 64), Vector(0, 1, 0)),
            new("P03", new string('d', 64), Vector(0, 0, 1)),
        ]);
        var singleton = matcher.RankRepresentatives([Vector(1, 0, 0)]);
        Assert.Equal(new[] { "P01", "P02", "P03" },
            singleton.Select(r => r.Alias).ToArray());

        var multi = matcher.RankRepresentatives(
            [Vector(1, 0, 0), Vector(.95f, .31f, 0)]);
        Assert.Equal("P01", multi[0].Alias);
        Assert.Equal(3, multi.Count);
        Assert.Throws<ArgumentException>(() =>
            matcher.RankRepresentatives([]));
        Assert.Throws<ArgumentException>(() =>
            matcher.RankRepresentatives(Enumerable.Repeat(Vector(1, 0, 0), 6).ToArray()));
    }

    [Fact]
    public async Task Explicit_case_scope_requires_several_unique_local_clips_and_pseudonyms()
    {
        string root = Path.Combine(Path.GetTempPath(), "wi0155-catalogue-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string repo = Directory.CreateDirectory(Path.Combine(root, "repo")).FullName;
            string casesDir = Directory.CreateDirectory(Path.Combine(root, "cases")).FullName;
            string clip1 = Directory.CreateDirectory(Path.Combine(root, "clip1")).FullName;
            string clip2 = Directory.CreateDirectory(Path.Combine(root, "clip2")).FullName;
            string first = Path.Combine(clip1, "frames.json");
            string second = Path.Combine(clip2, "frames.json");
            File.WriteAllText(first, "{}");
            File.WriteAllText(second, "{}");
            var selection = new VideoFaceCatalogueCases(1,
                [new VideoFaceCataloguePerson("P01", Guid.NewGuid())],
                [new VideoFaceCatalogueCase("V01", first),
                 new VideoFaceCatalogueCase("V02", second)]);

            VideoFaceCatalogueCasesLoader.Validate(selection, repo, casesDir);
            VideoFaceCatalogueCases all = selection with
            {
                People = null,
                CatalogueScope = "all-eligible"
            };
            VideoFaceCatalogueCasesLoader.Validate(all, repo, casesDir);
            Assert.Throws<ArgumentException>(() =>
                VideoFaceCatalogueCasesLoader.Validate(all with
                {
                    People = selection.People
                }, repo, casesDir));
            Assert.Throws<ArgumentException>(() =>
                VideoFaceCatalogueCasesLoader.Validate(selection with
                {
                    CatalogueScope = "all-eligible"
                }, repo, casesDir));
            Assert.Throws<ArgumentException>(() =>
                VideoFaceCatalogueCasesLoader.Validate(all with
                {
                    CatalogueScope = "typo"
                }, repo, casesDir));

            Assert.Throws<ArgumentException>(() =>
                VideoFaceCatalogueCasesLoader.Validate(selection with
                {
                    Clips = [new VideoFaceCatalogueCase("V01", first)]
                }, repo, casesDir));
            Assert.Throws<ArgumentException>(() =>
                VideoFaceCatalogueCasesLoader.Validate(selection with
                {
                    Clips = [new VideoFaceCatalogueCase("V01", first),
                             new VideoFaceCatalogueCase("V01", second)]
                }, repo, casesDir));
            Assert.Throws<ArgumentException>(() =>
                VideoFaceCatalogueCasesLoader.Validate(selection with
                {
                    People = [new VideoFaceCataloguePerson("Alice", Guid.NewGuid())]
                }, repo, casesDir));

            string reportDirectory = Directory.CreateDirectory(Path.Combine(root, "reviews")).FullName;
            string reportPath = Path.Combine(reportDirectory, "report.json");
            var evidence = new VideoFaceCatalogueProvenance(
                DateTimeOffset.UtcNow, "1:1:", "sface-2021dec-fp32",
                new string('a', 64), "sface-five-point-v1", 1, 1, 1, 12, 17);
            var report = new VideoFaceCatalogueEvaluationReport(1, evidence, []);
            await VideoFaceCatalogueReportWriter.WriteAsync(
                reportPath, repo, Path.Combine(casesDir, "cases.json"),
                [first, second], report);
            string json = File.ReadAllText(reportPath);
            Assert.DoesNotContain(first, json, StringComparison.Ordinal);
            Assert.DoesNotContain(second, json, StringComparison.Ordinal);
            Assert.DoesNotContain(selection.People![0].PersonId.ToString(), json, StringComparison.Ordinal);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                VideoFaceCatalogueReportWriter.WriteAsync(reportPath, repo,
                    Path.Combine(casesDir, "cases.json"), [first, second], report));
            string mapPath = Path.Combine(reportDirectory, "people-map.json");
            Guid personId = selection.People![0].PersonId;
            var map = new VideoFaceCataloguePrivatePersonMap(1, "1:1:",
            [
                new VideoFaceCataloguePrivatePersonMapEntry(
                    "P0001", personId, "Private display name")
            ]);
            await VideoFaceCataloguePrivatePeopleMapWriter.WriteAsync(
                mapPath, repo, Path.Combine(casesDir, "cases.json"),
                [first, second], reportPath, map);
            string mapJson = File.ReadAllText(mapPath);
            Assert.Contains(personId.ToString(), mapJson, StringComparison.Ordinal);
            Assert.Contains("Private display name", mapJson, StringComparison.Ordinal);
            Assert.DoesNotContain(personId.ToString(), json, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() =>
                VideoFaceCataloguePrivatePeopleMapWriter.ValidateTarget(
                    mapPath, repo, Path.Combine(casesDir, "cases.json"),
                    [first, second], reportPath));
            Assert.Throws<ArgumentException>(() =>
                VideoFaceCataloguePrivatePeopleMapWriter.ValidateTarget(
                    reportPath, repo, Path.Combine(casesDir, "cases.json"),
                    [first, second], reportPath));

        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Exact_model_mismatch_is_rejected_before_database_connection()
    {
        var cases = new VideoFaceCatalogueCases(1,
            [new VideoFaceCataloguePerson("P01", Guid.NewGuid())],
            [new VideoFaceCatalogueCase("V01", "unused"),
             new VideoFaceCatalogueCase("V02", "unused")]);
        var wrongModel = new ModelDescriptor(
            new ModelId("other-sface"),
            ModelRole.FaceEmbedding, ModelFormat.Onnx,
            new Sha256Digest(new string('b', 64)),
            new ImageSize(112, 112), "onnx", "Apache-2.0", "test",
            outputDimensions: 128, distanceMetric: DistanceMetric.Cosine,
            alignmentProtocol: new AlignmentProtocolId("sface-five-point-v1"));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await VideoFaceCatalogueEvidenceReader.LoadAsync(cases, wrongModel));
    }
}
