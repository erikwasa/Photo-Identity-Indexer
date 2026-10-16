using System.Security.Cryptography;
using System.Text;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Imaging;

/// <summary>
/// Evaluation-only metadata used to choose a representative photo-quality benchmark.
/// It is derived from catalogue facts and is not canonical photo metadata.
/// </summary>
public sealed record PhotoQualityBenchmarkDescriptor(
    AssetRevisionId RevisionId,
    string ContentSha256,
    string SourceKey,
    string? MediaType,
    int? Width,
    int? Height,
    DateTime? TakenAtLocal,
    string? CameraMake,
    string? CameraModel,
    int FaceCount);

public sealed record PhotoQualityBenchmarkStratum(
    string CaptureEra,
    string DeviceFamily,
    string MediaFamily,
    string Resolution,
    string Orientation,
    string Faces)
{
    public string Key =>
        $"era={CaptureEra};device={DeviceFamily};media={MediaFamily};resolution={Resolution};orientation={Orientation};faces={Faces}";
}

public sealed record PhotoQualityBenchmarkSelection(
    PhotoQualityBenchmarkDescriptor Photo,
    PhotoQualityBenchmarkStratum Stratum,
    string StableOrderKey);

public enum PhotoQualityBenchmarkPartition
{
    Calibration,
    HeldOut,
}

public sealed record PhotoQualityBenchmarkPartitionAssignment(
    PhotoQualityBenchmarkSelection Selection,
    PhotoQualityBenchmarkPartition Partition);

/// <summary>
/// Provider-independent deterministic selection/split policy for M37.
/// Selection balances the marginal archive distribution across several observable catalogue strata
/// instead of taking one stable pseudo-random slice.
/// </summary>
public static class PhotoQualityBenchmarkSelector
{
    public const string RepresentativeSelectionPolicyVersion = "m37-stratified-marginal-balance-v1";
    public const string PartitionPolicyVersion = "m37-calibration60-heldout40-leakage-groups-v1";

    public static IReadOnlyList<PhotoQualityBenchmarkSelection> SelectRepresentative(
        IEnumerable<PhotoQualityBenchmarkDescriptor> candidates,
        int targetCount)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (targetCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetCount));
        }

        PhotoQualityBenchmarkDescriptor[] population = candidates.ToArray();
        EnsureUniqueRevisions(population);
        if (population.Length == 0)
        {
            return [];
        }

        int desired = Math.Min(targetCount, population.Length);
        Dictionary<AssetRevisionId, PhotoQualityBenchmarkStratum> strata = population
            .ToDictionary(photo => photo.RevisionId, DescribeStratum);
        Dictionary<AssetRevisionId, string> orderKeys = population
            .ToDictionary(photo => photo.RevisionId, photo => StableHash(photo.RevisionId.ToString()));

        DimensionTargets targets = new(
            BuildTargets(population, desired, photo => strata[photo.RevisionId].CaptureEra),
            BuildTargets(population, desired, photo => strata[photo.RevisionId].DeviceFamily),
            BuildTargets(population, desired, photo => strata[photo.RevisionId].MediaFamily),
            BuildTargets(population, desired, photo => strata[photo.RevisionId].Resolution),
            BuildTargets(population, desired, photo => strata[photo.RevisionId].Orientation),
            BuildTargets(population, desired, photo => strata[photo.RevisionId].Faces));

        DimensionCounts counts = new();
        HashSet<AssetRevisionId> selectedIds = [];
        List<PhotoQualityBenchmarkSelection> selected = new(desired);

        while (selected.Count < desired)
        {
            PhotoQualityBenchmarkDescriptor? best = null;
            double bestScore = double.NegativeInfinity;
            string? bestOrderKey = null;

            foreach (PhotoQualityBenchmarkDescriptor candidate in population)
            {
                if (selectedIds.Contains(candidate.RevisionId))
                {
                    continue;
                }

                PhotoQualityBenchmarkStratum stratum = strata[candidate.RevisionId];
                double score =
                    DeficitScore(counts.CaptureEra, targets.CaptureEra, stratum.CaptureEra) +
                    DeficitScore(counts.DeviceFamily, targets.DeviceFamily, stratum.DeviceFamily) +
                    DeficitScore(counts.MediaFamily, targets.MediaFamily, stratum.MediaFamily) +
                    DeficitScore(counts.Resolution, targets.Resolution, stratum.Resolution) +
                    DeficitScore(counts.Orientation, targets.Orientation, stratum.Orientation) +
                    DeficitScore(counts.Faces, targets.Faces, stratum.Faces);
                string orderKey = orderKeys[candidate.RevisionId];

                if (best is null ||
                    score > bestScore ||
                    (Math.Abs(score - bestScore) < 0.0000001d &&
                     string.CompareOrdinal(orderKey, bestOrderKey) < 0))
                {
                    best = candidate;
                    bestScore = score;
                    bestOrderKey = orderKey;
                }
            }

            if (best is null || bestOrderKey is null)
            {
                throw new InvalidOperationException("Representative benchmark selection exhausted candidates unexpectedly.");
            }

            PhotoQualityBenchmarkStratum chosen = strata[best.RevisionId];
            selectedIds.Add(best.RevisionId);
            Increment(counts.CaptureEra, chosen.CaptureEra);
            Increment(counts.DeviceFamily, chosen.DeviceFamily);
            Increment(counts.MediaFamily, chosen.MediaFamily);
            Increment(counts.Resolution, chosen.Resolution);
            Increment(counts.Orientation, chosen.Orientation);
            Increment(counts.Faces, chosen.Faces);
            selected.Add(new PhotoQualityBenchmarkSelection(best, chosen, bestOrderKey));
        }

        return selected
            .OrderBy(item => item.StableOrderKey, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<PhotoQualityBenchmarkPartitionAssignment> AssignPartitions(
        IEnumerable<PhotoQualityBenchmarkSelection> selections,
        IReadOnlyDictionary<AssetRevisionId, string>? nearDuplicateGroupByRevision = null,
        double calibrationFraction = 0.60d)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (calibrationFraction <= 0d || calibrationFraction >= 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(calibrationFraction));
        }

        PhotoQualityBenchmarkSelection[] items = selections.ToArray();
        EnsureUniqueRevisions(items.Select(item => item.Photo).ToArray());
        if (items.Length == 0)
        {
            return [];
        }

        int[] parent = Enumerable.Range(0, items.Length).ToArray();

        foreach (IGrouping<string, int> group in items
                     .Select((item, index) => (item.Photo.ContentSha256, index))
                     .GroupBy(pair => pair.ContentSha256, pair => pair.index, StringComparer.OrdinalIgnoreCase))
        {
            UnionGroup(group, parent);
        }

        if (nearDuplicateGroupByRevision is not null)
        {
            foreach (IGrouping<string, int> group in items
                         .Select((item, index) => (
                             Group: nearDuplicateGroupByRevision.TryGetValue(item.Photo.RevisionId, out string? value)
                                 ? value
                                 : null,
                             index))
                         .Where(pair => !string.IsNullOrWhiteSpace(pair.Group))
                         .GroupBy(pair => pair.Group!, pair => pair.index, StringComparer.Ordinal))
            {
                UnionGroup(group, parent);
            }
        }

        Dictionary<int, List<int>> components = [];
        for (int index = 0; index < items.Length; index++)
        {
            int root = Find(parent, index);
            if (!components.TryGetValue(root, out List<int>? members))
            {
                members = [];
                components.Add(root, members);
            }

            members.Add(index);
        }

        Component[] orderedComponents = components.Values
            .Select(members =>
            {
                string componentKey = string.Join(
                    "|",
                    members
                        .Select(index => items[index].Photo.RevisionId.ToString())
                        .OrderBy(value => value, StringComparer.Ordinal));
                return new Component(members.ToArray(), StableHash(componentKey));
            })
            .OrderBy(component => component.StableKey, StringComparer.Ordinal)
            .ToArray();

        int calibrationTarget = (int)Math.Round(
            items.Length * calibrationFraction,
            MidpointRounding.AwayFromZero);
        calibrationTarget = Math.Clamp(calibrationTarget, 0, items.Length);
        int heldOutTarget = items.Length - calibrationTarget;
        int calibrationCount = 0;
        int heldOutCount = 0;
        Dictionary<int, PhotoQualityBenchmarkPartition> partitionByIndex = [];

        foreach (Component component in orderedComponents)
        {
            int size = component.Members.Length;
            double calibrationError =
                Math.Abs(calibrationTarget - (calibrationCount + size)) +
                Math.Abs(heldOutTarget - heldOutCount);
            double heldOutError =
                Math.Abs(calibrationTarget - calibrationCount) +
                Math.Abs(heldOutTarget - (heldOutCount + size));

            PhotoQualityBenchmarkPartition partition;
            if (calibrationCount >= calibrationTarget)
            {
                partition = PhotoQualityBenchmarkPartition.HeldOut;
            }
            else if (heldOutCount >= heldOutTarget)
            {
                partition = PhotoQualityBenchmarkPartition.Calibration;
            }
            else if (calibrationError < heldOutError)
            {
                partition = PhotoQualityBenchmarkPartition.Calibration;
            }
            else if (heldOutError < calibrationError)
            {
                partition = PhotoQualityBenchmarkPartition.HeldOut;
            }
            else
            {
                partition = string.CompareOrdinal(component.StableKey, "8") < 0
                    ? PhotoQualityBenchmarkPartition.Calibration
                    : PhotoQualityBenchmarkPartition.HeldOut;
            }

            foreach (int index in component.Members)
            {
                partitionByIndex[index] = partition;
            }

            if (partition == PhotoQualityBenchmarkPartition.Calibration)
            {
                calibrationCount += size;
            }
            else
            {
                heldOutCount += size;
            }
        }

        return items
            .Select((item, index) => new PhotoQualityBenchmarkPartitionAssignment(
                item,
                partitionByIndex[index]))
            .OrderBy(item => item.Selection.StableOrderKey, StringComparer.Ordinal)
            .ToArray();
    }

    public static PhotoQualityBenchmarkStratum DescribeStratum(PhotoQualityBenchmarkDescriptor photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        int? captureYear = ResolveCaptureYear(photo.TakenAtLocal, photo.SourceKey);
        string captureEra = captureYear.HasValue
            ? $"{captureYear.Value / 10 * 10}s"
            : "unknown";
        string deviceFamily = DeviceFamily(photo.CameraMake, photo.CameraModel);
        string mediaFamily = MediaFamily(photo.MediaType, photo.SourceKey);
        string resolution = ResolutionBucket(photo.Width, photo.Height);
        string orientation = OrientationBucket(photo.Width, photo.Height);
        string faces = photo.FaceCount switch
        {
            <= 0 => "none",
            1 => "one",
            _ => "multiple",
        };

        return new PhotoQualityBenchmarkStratum(
            captureEra,
            deviceFamily,
            mediaFamily,
            resolution,
            orientation,
            faces);
    }

    public static int? ResolveCaptureYear(DateTime? takenAtLocal, string sourceKey)
    {
        if (takenAtLocal.HasValue)
        {
            return takenAtLocal.Value.Year;
        }

        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return null;
        }

        foreach (string segment in sourceKey.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Length < 4 ||
                !int.TryParse(segment.AsSpan(0, 4), out int year) ||
                year is < 1800 or > 2100)
            {
                continue;
            }

            return year;
        }

        return null;
    }

    private static string DeviceFamily(string? make, string? model)
    {
        string normalizedMake = NormalizeLabel(make);
        string normalizedModel = NormalizeLabel(model);
        if (normalizedModel.Length > 0 && normalizedMake.Length > 0)
        {
            return $"camera:{normalizedMake}/{normalizedModel}";
        }

        if (normalizedModel.Length > 0)
        {
            return $"camera:{normalizedModel}";
        }

        if (normalizedMake.Length > 0)
        {
            return $"make:{normalizedMake}";
        }

        return "unknown-camera";
    }

    private static string MediaFamily(string? mediaType, string sourceKey)
    {
        string normalizedType = (mediaType ?? string.Empty).Trim().ToLowerInvariant();
        string extension = Path.GetExtension(sourceKey ?? string.Empty).ToLowerInvariant();
        if (normalizedType.Contains("jpeg", StringComparison.Ordinal) ||
            extension is ".jpg" or ".jpeg")
        {
            return "jpeg";
        }

        if (normalizedType.Contains("heic", StringComparison.Ordinal) ||
            normalizedType.Contains("heif", StringComparison.Ordinal) ||
            extension is ".heic" or ".heif")
        {
            return "heic";
        }

        if (normalizedType.Contains("png", StringComparison.Ordinal) || extension == ".png")
        {
            return "png";
        }

        if (normalizedType.Contains("dng", StringComparison.Ordinal) ||
            normalizedType.Contains("raw", StringComparison.Ordinal) ||
            extension == ".dng")
        {
            return "raw";
        }

        if (normalizedType.Contains("webp", StringComparison.Ordinal) || extension == ".webp")
        {
            return "webp";
        }

        return "other";
    }

    private static string ResolutionBucket(int? width, int? height)
    {
        if (!width.HasValue || !height.HasValue || width <= 0 || height <= 0)
        {
            return "unknown";
        }

        long pixels = (long)width.Value * height.Value;
        return pixels switch
        {
            < 1_000_000L => "under-1mp",
            < 4_000_000L => "1-4mp",
            < 12_000_000L => "4-12mp",
            < 24_000_000L => "12-24mp",
            _ => "24mp-plus",
        };
    }

    private static string OrientationBucket(int? width, int? height)
    {
        if (!width.HasValue || !height.HasValue || width <= 0 || height <= 0)
        {
            return "unknown";
        }

        double ratio = width.Value / (double)height.Value;
        if (ratio > 1.05d)
        {
            return "landscape";
        }

        if (ratio < 1d / 1.05d)
        {
            return "portrait";
        }

        return "square";
    }

    private static Dictionary<string, int> BuildTargets(
        IReadOnlyList<PhotoQualityBenchmarkDescriptor> population,
        int desired,
        Func<PhotoQualityBenchmarkDescriptor, string> groupSelector)
    {
        GroupAllocation[] groups = population
            .GroupBy(groupSelector, StringComparer.Ordinal)
            .Select(group => new GroupAllocation(
                group.Key,
                group.Count(),
                desired * group.Count() / (double)population.Count))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        if (desired >= groups.Length)
        {
            foreach (GroupAllocation group in groups)
            {
                group.Target = 1;
            }
        }

        int allocated = groups.Sum(group => group.Target);
        while (allocated < desired)
        {
            GroupAllocation? next = groups
                .Where(group => group.Target < group.PopulationCount)
                .OrderByDescending(group => group.Ideal - group.Target)
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next is null)
            {
                break;
            }

            next.Target++;
            allocated++;
        }

        while (allocated > desired)
        {
            GroupAllocation? next = groups
                .Where(group => group.Target > 0)
                .OrderByDescending(group => group.Target - group.Ideal)
                .ThenByDescending(group => group.Target)
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next is null)
            {
                break;
            }

            next.Target--;
            allocated--;
        }

        return groups.ToDictionary(group => group.Key, group => group.Target, StringComparer.Ordinal);
    }

    private static double DeficitScore(
        IReadOnlyDictionary<string, int> counts,
        IReadOnlyDictionary<string, int> targets,
        string key)
    {
        if (!targets.TryGetValue(key, out int target) || target <= 0)
        {
            return 0d;
        }

        int current = counts.TryGetValue(key, out int value) ? value : 0;
        return current >= target
            ? 0d
            : (target - current) / (double)target;
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.TryGetValue(key, out int current)
            ? current + 1
            : 1;
    }

    private static void EnsureUniqueRevisions(IReadOnlyList<PhotoQualityBenchmarkDescriptor> photos)
    {
        AssetRevisionId? duplicate = photos
            .GroupBy(photo => photo.RevisionId)
            .FirstOrDefault(group => group.Count() > 1)?
            .Key;
        if (duplicate.HasValue)
        {
            throw new ArgumentException($"Benchmark input contains duplicate revision '{duplicate.Value}'.");
        }
    }

    private static void UnionGroup(IEnumerable<int> indices, int[] parent)
    {
        int[] members = indices.ToArray();
        if (members.Length < 2)
        {
            return;
        }

        int first = members[0];
        for (int index = 1; index < members.Length; index++)
        {
            Union(parent, first, members[index]);
        }
    }

    private static int Find(int[] parent, int value)
    {
        while (parent[value] != value)
        {
            parent[value] = parent[parent[value]];
            value = parent[value];
        }

        return value;
    }

    private static void Union(int[] parent, int left, int right)
    {
        int leftRoot = Find(parent, left);
        int rightRoot = Find(parent, right);
        if (leftRoot != rightRoot)
        {
            parent[rightRoot] = leftRoot;
        }
    }

    private static string StableHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string NormalizeLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(
                " ",
                value.Trim().ToLowerInvariant().Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));

    private sealed class GroupAllocation
    {
        public GroupAllocation(string key, int populationCount, double ideal)
        {
            Key = key;
            PopulationCount = populationCount;
            Ideal = ideal;
        }

        public string Key { get; }
        public int PopulationCount { get; }
        public double Ideal { get; }
        public int Target { get; set; }
    }

    private sealed record DimensionTargets(
        Dictionary<string, int> CaptureEra,
        Dictionary<string, int> DeviceFamily,
        Dictionary<string, int> MediaFamily,
        Dictionary<string, int> Resolution,
        Dictionary<string, int> Orientation,
        Dictionary<string, int> Faces);

    private sealed class DimensionCounts
    {
        public Dictionary<string, int> CaptureEra { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> DeviceFamily { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> MediaFamily { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Resolution { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Orientation { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Faces { get; } = new(StringComparer.Ordinal);
    }

    private sealed record Component(int[] Members, string StableKey);
}
