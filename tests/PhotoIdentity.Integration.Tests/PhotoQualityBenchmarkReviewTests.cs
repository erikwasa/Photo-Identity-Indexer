using System.Security.Cryptography;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.PhotoQualityEvaluation;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class PhotoQualityBenchmarkReviewTests
{
    [Fact]
    public async Task Review_store_saves_resumes_and_creates_one_backup()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"photoidentity-quality-review-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            byte[] one = [1, 2, 3, 4, 5];
            byte[] two = [6, 7, 8];
            await File.WriteAllBytesAsync(Path.Combine(root, "one.jpg"), one);
            await File.WriteAllBytesAsync(Path.Combine(root, "two.jpg"), two);

            string manifestPath = Path.Combine(root, "benchmark.json");
            PhotoQualityBenchmarkExport manifest = Manifest(
                "bench-a",
                Photo("rev-1", "one.jpg", one),
                Photo("rev-2", "two.jpg", two));
            await PhotoQualityBenchmarkFile.SaveAtomicAsync(
                manifestPath,
                manifest);

            PhotoQualityBenchmarkReviewStore store =
                await PhotoQualityBenchmarkReviewStore.OpenAsync(
                    manifestPath,
                    root);
            PhotoQualityBlindReviewState start = store.GetState(0);
            await store.SaveReviewAsync(
                start.RevisionId,
                new PhotoQualityBlindReviewUpdate(
                    "high",
                    true,
                    "clear subject"));

            string backupPath = manifestPath + ".before-review.bak";
            Assert.True(File.Exists(backupPath));
            string backupBefore = await File.ReadAllTextAsync(backupPath);

            await store.SaveReviewAsync(
                start.RevisionId,
                new PhotoQualityBlindReviewUpdate(
                    "very-high",
                    true,
                    "updated"));
            Assert.Equal(
                backupBefore,
                await File.ReadAllTextAsync(backupPath));

            PhotoQualityBenchmarkReviewStore resumed =
                await PhotoQualityBenchmarkReviewStore.OpenAsync(
                    manifestPath,
                    root);
            PhotoQualityBlindReviewState saved = Enumerable.Range(0, 2)
                .Select(resumed.GetState)
                .Single(state => state.RevisionId == start.RevisionId);
            Assert.Equal("very-high", saved.QualityLevel);
            Assert.True(saved.PersonallyImportant);
            Assert.Equal("updated", saved.Notes);
            Assert.Equal(1, saved.Reviewed);

            byte[] loaded = await resumed.ReadProxyAsync(start.RevisionId);
            Assert.NotEmpty(loaded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Blind_order_is_stable_for_a_benchmark()
    {
        PhotoQualityBenchmarkExport benchmark = Manifest(
            "bench-order",
            Photo("a", "a.jpg", [1]),
            Photo("b", "b.jpg", [2]),
            Photo("c", "c.jpg", [3]));

        string[] order1 = benchmark.Photos
            .OrderBy(
                photo => PhotoQualityBenchmarkFile.StableBlindOrderKey(
                    benchmark.BenchmarkId,
                    photo.RevisionId),
                StringComparer.Ordinal)
            .Select(photo => photo.RevisionId)
            .ToArray();
        string[] order2 = benchmark.Photos
            .Reverse()
            .OrderBy(
                photo => PhotoQualityBenchmarkFile.StableBlindOrderKey(
                    benchmark.BenchmarkId,
                    photo.RevisionId),
                StringComparer.Ordinal)
            .Select(photo => photo.RevisionId)
            .ToArray();

        Assert.Equal(order1, order2);
    }

    [Fact]
    public void Challenge_merge_preserves_frozen_core_and_clears_review()
    {
        PhotoQualityBenchmarkExport benchmark = Manifest(
            "bench-challenge",
            Photo("core-a", "a.jpg", [1]),
            Photo("core-b", "b.jpg", [2])) with
        {
            RepresentativeCount = 2,
            RepresentativeTarget = 2,
            ChallengeReserve = 2,
            ChallengeCount = 0,
            CalibrationCount = 1,
            HeldOutCount = 1,
        };
        PhotoQualityBenchmarkPhoto challenge = Photo(
            "challenge-a",
            "c.jpg",
            [3]) with
        {
            Partition = "held-out",
            SampleKind = "representative",
            ChallengeReasons = ["model-disagreement"],
            Review = new PhotoQualityBenchmarkHumanReview(
                "high",
                true,
                "must clear"),
        };

        PhotoQualityBenchmarkExport merged =
            PhotoQualityBenchmarkChallengeMerger.Merge(
                benchmark,
                new PhotoQualityBenchmarkChallengeFile(
                    1,
                    benchmark.BenchmarkId,
                    "external-disagreement-v1",
                    [challenge]));

        Assert.Equal(2, merged.RepresentativeCount);
        Assert.Equal(1, merged.CalibrationCount);
        Assert.Equal(1, merged.HeldOutCount);
        Assert.Equal(1, merged.ChallengeCount);

        PhotoQualityBenchmarkPhoto added =
            Assert.Single(
                merged.Photos,
                photo => photo.RevisionId == "challenge-a");
        Assert.Equal("challenge", added.Partition);
        Assert.Equal("challenge", added.SampleKind);
        Assert.Null(added.Review.QualityLevel);
        Assert.Null(added.Review.PersonallyImportant);
        Assert.Contains(
            "external-disagreement-v1",
            merged.ChallengeSelectionPolicies!);
    }

    [Fact]
    public void Challenge_merge_rejects_overlap_with_representative_core()
    {
        PhotoQualityBenchmarkExport benchmark = Manifest(
            "bench-overlap",
            Photo("core-a", "a.jpg", [1])) with
        {
            ChallengeReserve = 2,
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => PhotoQualityBenchmarkChallengeMerger.Merge(
                benchmark,
                new PhotoQualityBenchmarkChallengeFile(
                    1,
                    benchmark.BenchmarkId,
                    "test",
                    [Photo("core-a", "duplicate.jpg", [2])])));

        Assert.Contains("overlap", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PhotoQualityBenchmarkExport Manifest(
        string benchmarkId,
        params PhotoQualityBenchmarkPhoto[] photos)
    {
        PhotoQualityBenchmarkCoverage coverage =
            PhotoQualityBenchmarkFile.Coverage(photos);
        return new PhotoQualityBenchmarkExport(
            2,
            benchmarkId,
            DateTimeOffset.UtcNow,
            "profile-id=jpeg-1600-q78",
            "test-selection",
            "test-partition",
            photos.Length,
            photos.Length,
            photos.Length,
            0,
            0,
            photos.Length,
            0,
            new PhotoQualityBenchmarkLeakageSummary(
                "test",
                4,
                0,
                0),
            coverage,
            coverage,
            [],
            null,
            "test",
            photos);
    }

    private static PhotoQualityBenchmarkPhoto Photo(
        string revisionId,
        string relativePath,
        byte[] proxy)
    {
        string hash = Convert.ToHexString(
            SHA256.HashData(proxy)).ToLowerInvariant();
        return new PhotoQualityBenchmarkPhoto(
            revisionId,
            $"private/{revisionId}.jpg",
            new string('a', 64),
            relativePath,
            hash,
            "image/jpeg",
            4000,
            3000,
            new DateTime(2020, 1, 1),
            "Example",
            "Camera",
            1,
            new PhotoQualityBenchmarkStratum(
                "2020s",
                "camera:example/camera",
                "jpeg",
                "12-24mp",
                "landscape",
                "one"),
            "calibration",
            "representative",
            [],
            new PhotoQualityBenchmarkHumanReview(
                null,
                null,
                null));
    }
}
