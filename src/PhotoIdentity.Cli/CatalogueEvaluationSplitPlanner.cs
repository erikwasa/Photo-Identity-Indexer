using System.Security.Cryptography;
using System.Text;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Cli;

internal static class CatalogueEvaluationSplitPlanner
{
    public static CatalogueEvaluationSplitPlan Create(
        CatalogueEvaluationExportInput input,
        CatalogueEvaluationSplitOptions options)
    {
        if (input.Faces.Count == 0)
        {
            throw new ArgumentException(
                "No human-assigned faces in the selected scope have both requested model revisions.");
        }

        int[] dimensions = input.Faces.Select(face => face.Dimensions).Distinct().ToArray();
        if (dimensions.Length != 1)
        {
            throw new InvalidDataException("The selected embedder revision has inconsistent stored dimensions.");
        }

        IReadOnlyDictionary<AssetRevisionId, CatalogueEvaluationSourceRevision> revisions = input.SourceRevisions
            .ToDictionary(revision => revision.Id);
        IReadOnlyDictionary<AssetRevisionId, int> faceCounts = input.Faces
            .GroupBy(face => face.AssetRevisionId)
            .ToDictionary(group => group.Key, group => group.Count());
        PlannedFace[] faces = input.Faces
            .Select(face => WithTiming(face, revisions[face.AssetRevisionId], faceCounts[face.AssetRevisionId]))
            .ToArray();
        PhotoGroup[] groups = faces
            .GroupBy(face => face.Face.AssetRevisionId)
            .Select(group => new PhotoGroup(
                group.Key,
                group.OrderBy(face => face.Face.Id.ToString(), StringComparer.Ordinal).ToArray()))
            .OrderBy(group => group.Id.ToString(), StringComparer.Ordinal)
            .ToArray();

        Dictionary<AssetRevisionId, string> assignments = [];
        List<PersonAllocation> known = [];
        PersonId[] people = faces
            .Select(face => face.Face.PersonId)
            .Distinct()
            .OrderBy(person => Key(options.Seed, "person", person.ToString()), StringComparer.Ordinal)
            .ThenBy(person => person.ToString(), StringComparer.Ordinal)
            .ToArray();
        foreach (PersonId person in people)
        {
            if (TryAllocatePerson(person, groups, assignments, options, out PersonAllocation? allocation))
            {
                known.Add(allocation!);
            }
        }

        if (known.Count == 0)
        {
            int required = options.GalleryPerPerson + options.ValidationKnownPerPerson + options.TestKnownPerPerson;
            int available = people
                .Select(person => groups.Count(group => group.Faces.Any(face => face.Face.PersonId == person)))
                .DefaultIfEmpty(0)
                .Max();
            throw new ArgumentException(
                $"Insufficient known examples: at least one assigned person needs {required} distinct source photos " +
                $"({options.GalleryPerPerson} gallery, {options.ValidationKnownPerPerson} validation and " +
                $"{options.TestKnownPerPerson} test); the best available person has {available}.");
        }

        HashSet<PersonId> knownPeople = known.Select(item => item.PersonId).ToHashSet();
        HashSet<AssetRevisionId> unknownUsed = [];
        PlannedFace[] validationUnknown = ReserveUnknown(
            "validation",
            options.ValidationUnknownCount,
            groups,
            assignments,
            knownPeople,
            unknownUsed,
            options.Seed);
        PlannedFace[] testUnknown = ReserveUnknown(
            "test",
            options.TestUnknownCount,
            groups,
            assignments,
            knownPeople,
            unknownUsed,
            options.Seed);

        PlannedFace[] gallery = known
            .SelectMany(item => item.Gallery.Select(group => Pick(group, item.PersonId, options.Seed, "gallery")))
            .OrderBy(face => face.Face.PersonId.ToString(), StringComparer.Ordinal)
            .ThenBy(face => face.Face.Id.ToString(), StringComparer.Ordinal)
            .ToArray();
        PlannedFace[] validationKnown = known
            .SelectMany(item => item.Validation.Select(group => Pick(group, item.PersonId, options.Seed, "validation")))
            .OrderBy(face => face.Face.Id.ToString(), StringComparer.Ordinal)
            .ToArray();
        PlannedFace[] testKnown = known
            .SelectMany(item => item.Test.Select(group => Pick(group, item.PersonId, options.Seed, "test")))
            .OrderBy(face => face.Face.Id.ToString(), StringComparer.Ordinal)
            .ToArray();

        PlannedFace[] selected = gallery
            .Concat(validationKnown)
            .Concat(testKnown)
            .Concat(validationUnknown)
            .Concat(testUnknown)
            .ToArray();
        return new CatalogueEvaluationSplitPlan(
            dimensions[0],
            gallery,
            validationKnown,
            testKnown,
            validationUnknown,
            testUnknown,
            known.Count,
            selected.Count(face => face.UsesTimingFallback));
    }

    private static PlannedFace WithTiming(
        CatalogueEvaluationFace face,
        CatalogueEvaluationSourceRevision revision,
        int revisionFaceCount)
    {
        if (revision.ProcessingStartedAtUtc is DateTimeOffset started &&
            revision.ProcessingCompletedAtUtc is DateTimeOffset completed)
        {
            double duration = (completed - started).TotalMilliseconds;
            double perFace = duration / revisionFaceCount;
            if (duration > 0 && double.IsFinite(perFace) && perFace > 0)
            {
                return new PlannedFace(face, perFace, false);
            }
        }

        return new PlannedFace(face, 1d, true);
    }

    private static bool TryAllocatePerson(
        PersonId person,
        IReadOnlyList<PhotoGroup> groups,
        IDictionary<AssetRevisionId, string> assignments,
        CatalogueEvaluationSplitOptions options,
        out PersonAllocation? allocation)
    {
        Dictionary<AssetRevisionId, string> trial = new(assignments);
        HashSet<AssetRevisionId> used = [];
        PhotoGroup[]? gallery = ReservePerson(
            person, "gallery", options.GalleryPerPerson, groups, trial, used, options.Seed);
        PhotoGroup[]? validation = gallery is null ? null : ReservePerson(
            person, "validation", options.ValidationKnownPerPerson, groups, trial, used, options.Seed);
        PhotoGroup[]? test = validation is null ? null : ReservePerson(
            person, "test", options.TestKnownPerPerson, groups, trial, used, options.Seed);
        if (gallery is null || validation is null || test is null)
        {
            allocation = null;
            return false;
        }

        assignments.Clear();
        foreach ((AssetRevisionId revision, string split) in trial)
        {
            assignments[revision] = split;
        }
        allocation = new PersonAllocation(person, gallery, validation, test);
        return true;
    }

    private static PhotoGroup[]? ReservePerson(
        PersonId person,
        string split,
        int count,
        IReadOnlyList<PhotoGroup> groups,
        IDictionary<AssetRevisionId, string> assignments,
        ISet<AssetRevisionId> used,
        string seed)
    {
        PhotoGroup[] selected = groups
            .Where(group =>
                !used.Contains(group.Id) &&
                group.Faces.Any(face => face.Face.PersonId == person) &&
                (!assignments.TryGetValue(group.Id, out string? assigned) || assigned == split))
            .OrderBy(group => assignments.ContainsKey(group.Id) ? 0 : 1)
            .ThenBy(group => Key(seed, $"{split}:{person}", group.Id.ToString()), StringComparer.Ordinal)
            .ThenBy(group => group.Id.ToString(), StringComparer.Ordinal)
            .Take(count)
            .ToArray();
        if (selected.Length != count)
        {
            return null;
        }

        foreach (PhotoGroup group in selected)
        {
            used.Add(group.Id);
            assignments[group.Id] = split;
        }
        return selected;
    }

    private static PlannedFace[] ReserveUnknown(
        string split,
        int count,
        IReadOnlyList<PhotoGroup> groups,
        IDictionary<AssetRevisionId, string> assignments,
        IReadOnlySet<PersonId> knownPeople,
        ISet<AssetRevisionId> used,
        string seed)
    {
        PhotoGroup[] selected = groups
            .Where(group =>
                !used.Contains(group.Id) &&
                group.Faces.Any(face => !knownPeople.Contains(face.Face.PersonId)) &&
                (!assignments.TryGetValue(group.Id, out string? assigned) || assigned == split))
            .OrderBy(group => assignments.ContainsKey(group.Id) ? 0 : 1)
            .ThenBy(group => Key(seed, $"{split}:unknown", group.Id.ToString()), StringComparer.Ordinal)
            .ThenBy(group => group.Id.ToString(), StringComparer.Ordinal)
            .Take(count)
            .ToArray();
        if (selected.Length != count)
        {
            int available = groups.Count(group =>
                !used.Contains(group.Id) &&
                group.Faces.Any(face => !knownPeople.Contains(face.Face.PersonId)) &&
                (!assignments.TryGetValue(group.Id, out string? assigned) || assigned == split));
            throw new ArgumentException(
                $"Insufficient unknown examples for {split}: requested {count} distinct source photos from assigned people absent from the gallery, but only {available} are available.");
        }

        return selected.Select(group =>
        {
            used.Add(group.Id);
            assignments[group.Id] = split;
            return group.Faces
                .Where(face => !knownPeople.Contains(face.Face.PersonId))
                .OrderBy(face => Key(seed, $"{split}:unknown-face", face.Face.Id.ToString()), StringComparer.Ordinal)
                .ThenBy(face => face.Face.Id.ToString(), StringComparer.Ordinal)
                .First();
        }).ToArray();
    }

    private static PlannedFace Pick(PhotoGroup group, PersonId person, string seed, string split) =>
        group.Faces
            .Where(face => face.Face.PersonId == person)
            .OrderBy(face => Key(seed, $"{split}:{person}:face", face.Face.Id.ToString()), StringComparer.Ordinal)
            .ThenBy(face => face.Face.Id.ToString(), StringComparer.Ordinal)
            .First();

    private static string Key(string seed, string category, string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}\n{category}\n{value}")))
            .ToLowerInvariant();

    private sealed record PhotoGroup(AssetRevisionId Id, IReadOnlyList<PlannedFace> Faces);
    private sealed record PersonAllocation(
        PersonId PersonId,
        IReadOnlyList<PhotoGroup> Gallery,
        IReadOnlyList<PhotoGroup> Validation,
        IReadOnlyList<PhotoGroup> Test);
}

internal sealed record CatalogueEvaluationSplitOptions(
    string Seed,
    int GalleryPerPerson,
    int ValidationKnownPerPerson,
    int TestKnownPerPerson,
    int ValidationUnknownCount,
    int TestUnknownCount);

internal sealed record PlannedFace(
    CatalogueEvaluationFace Face,
    double ElapsedMilliseconds,
    bool UsesTimingFallback);

internal sealed record CatalogueEvaluationSplitPlan(
    int Dimensions,
    IReadOnlyList<PlannedFace> Gallery,
    IReadOnlyList<PlannedFace> ValidationKnown,
    IReadOnlyList<PlannedFace> TestKnown,
    IReadOnlyList<PlannedFace> ValidationUnknown,
    IReadOnlyList<PlannedFace> TestUnknown,
    int KnownPersonCount,
    int FallbackTimingSampleCount);
