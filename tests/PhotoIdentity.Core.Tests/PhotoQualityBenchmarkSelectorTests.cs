using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class PhotoQualityBenchmarkSelectorTests
{
    [Fact]
    public void Representative_selection_is_deterministic_and_balances_observable_strata()
    {
        PhotoQualityBenchmarkDescriptor[] population =
        [
            Descriptor(1, "2026/01/a.jpg", "image/jpeg", 4000, 3000, "Apple", "Phone A", 0),
            Descriptor(2, "2026/01/b.jpg", "image/jpeg", 4000, 3000, "Apple", "Phone A", 0),
            Descriptor(3, "2026/01/c.jpg", "image/jpeg", 4000, 3000, "Apple", "Phone A", 0),
            Descriptor(4, "2026/01/d.jpg", "image/jpeg", 4000, 3000, "Apple", "Phone A", 0),
            Descriptor(5, "2016/01/e.heic", "image/heic", 3000, 4000, "Apple", "Phone B", 1),
            Descriptor(6, "2016/01/f.heic", "image/heic", 3000, 4000, "Apple", "Phone B", 1),
            Descriptor(7, "2006/scan/g.dng", "image/x-adobe-dng", 800, 600, "Canon", "ScanCam", 2),
            Descriptor(8, "2006/scan/h.dng", "image/x-adobe-dng", 800, 600, "Canon", "ScanCam", 2),
        ];

        IReadOnlyList<PhotoQualityBenchmarkSelection> first =
            PhotoQualityBenchmarkSelector.SelectRepresentative(population, 5);
        IReadOnlyList<PhotoQualityBenchmarkSelection> second =
            PhotoQualityBenchmarkSelector.SelectRepresentative(population.Reverse(), 5);

        Assert.Equal(
            first.Select(item => item.Photo.RevisionId),
            second.Select(item => item.Photo.RevisionId));
        Assert.Equal(5, first.Count);
        Assert.Contains(first, item => item.Stratum.MediaFamily == "heic");
        Assert.Contains(first, item => item.Stratum.MediaFamily == "raw");
        Assert.Contains(first, item => item.Stratum.Faces == "none");
        Assert.Contains(first, item => item.Stratum.Faces == "one");
        Assert.Contains(first, item => item.Stratum.Faces == "multiple");
    }

    [Fact]
    public void Capture_year_falls_back_to_a_year_prefixed_source_segment()
    {
        PhotoQualityBenchmarkDescriptor photo = Descriptor(
            1,
            "Moa/20260216_141216971_iOS.heic",
            "image/heic",
            3000,
            4000,
            null,
            null,
            0,
            takenAtLocal: null);

        PhotoQualityBenchmarkStratum stratum =
            PhotoQualityBenchmarkSelector.DescribeStratum(photo);

        Assert.Equal(2026, PhotoQualityBenchmarkSelector.ResolveCaptureYear(null, photo.SourceKey));
        Assert.Equal("2020s", stratum.CaptureEra);
        Assert.Equal("unknown-camera", stratum.DeviceFamily);
    }

    [Fact]
    public void Partition_assignment_keeps_exact_and_near_duplicate_groups_together()
    {
        PhotoQualityBenchmarkDescriptor[] population = Enumerable.Range(1, 10)
            .Select(index => Descriptor(
                index,
                $"2026/{index:D2}.jpg",
                "image/jpeg",
                4000,
                3000,
                "Apple",
                "Phone",
                index % 3))
            .ToArray();

        population[1] = population[1] with { ContentSha256 = population[0].ContentSha256 };

        IReadOnlyList<PhotoQualityBenchmarkSelection> selected =
            PhotoQualityBenchmarkSelector.SelectRepresentative(population, 10);
        Dictionary<AssetRevisionId, string> nearGroups = new()
        {
            [population[2].RevisionId] = "near-a",
            [population[3].RevisionId] = "near-a",
        };

        IReadOnlyList<PhotoQualityBenchmarkPartitionAssignment> assignments =
            PhotoQualityBenchmarkSelector.AssignPartitions(selected, nearGroups);

        Dictionary<AssetRevisionId, PhotoQualityBenchmarkPartition> byRevision =
            assignments.ToDictionary(item => item.Selection.Photo.RevisionId, item => item.Partition);

        Assert.Equal(byRevision[population[0].RevisionId], byRevision[population[1].RevisionId]);
        Assert.Equal(byRevision[population[2].RevisionId], byRevision[population[3].RevisionId]);

        int calibration = assignments.Count(item => item.Partition == PhotoQualityBenchmarkPartition.Calibration);
        Assert.InRange(calibration, 5, 7);
    }

    private static PhotoQualityBenchmarkDescriptor Descriptor(
        int index,
        string sourceKey,
        string mediaType,
        int width,
        int height,
        string? cameraMake,
        string? cameraModel,
        int faces,
        DateTime? takenAtLocal = null)
    {
        AssetRevisionId revisionId = AssetRevisionId.From(
            Guid.Parse($"00000000-0000-0000-0000-{index:D12}"));
        return new PhotoQualityBenchmarkDescriptor(
            revisionId,
            index.ToString("x64"),
            sourceKey,
            mediaType,
            width,
            height,
            takenAtLocal,
            cameraMake,
            cameraModel,
            faces);
    }
}
