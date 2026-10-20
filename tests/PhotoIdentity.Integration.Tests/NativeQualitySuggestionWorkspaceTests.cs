using PhotoIdentity.Api;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class NativeQualitySuggestionWorkspaceTests
{
    private static readonly string A = "00000000-0000-0000-0000-000000000001";
    private static readonly string B = "00000000-0000-0000-0000-000000000002";
    private static readonly string C = "00000000-0000-0000-0000-000000000003";

    [Fact]
    public async Task Native_state_persists_context_separation_and_manual_follow_up()
    {
        string dir = TempPath();
        try
        {
            QualitySuggestionWorkspace store = new(dir);
            Assert.Empty((await store.ReadAsync()).Candidates);
            Assert.True((await store.ReadAsync()).EnabledScopes["archive-cleanup"]);
            Assert.False((await store.ReadAsync()).EnabledScopes["slideshow-curation"]);

            await store.ReplaceCandidatesAsync(80,
            [
                Candidate(A, "candidate-low-sharpness", 21),
                Candidate(B, "candidate-severe-underexposure", 41)
            ]);
            await store.DecideAsync("archive-cleanup", A, "keep");
            await store.DecideAsync("archive-cleanup", B, "consider-exclusion");

            QualitySuggestionWorkspace resumed = new(dir);
            QualitySuggestionSnapshot saved = await resumed.ReadAsync();
            Assert.Equal(2, saved.Candidates.Count);
            Assert.Equal("keep", saved.Decisions[QualitySuggestionWorkspace.Key("archive-cleanup", A)]);

            QualitySuggestionPage following = QualitySuggestionWorkspace.Page(
                saved, "archive-cleanup", "all", "consider-exclusion", 0, 30);
            Assert.Single(following.Items);
            Assert.Equal(B, following.Items[0].RevisionId);
            Assert.EndsWith("/photo/" + B, following.Items[0].PhotoUrl);
            Assert.Equal(2, following.ReviewedInScope);
            Assert.Equal(1, following.ConsiderExclusionInScope);

            // No default cross-use-case effects: this photo is unreviewed
            // in slideshow/collection contexts and those contexts are disabled.
            Assert.Empty(saved.Decisions.Where(x =>
                x.Key.StartsWith("slideshow-curation/", StringComparison.Ordinal)));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resumed.DecideAsync("slideshow-curation", B, "consider-exclusion"));

            await resumed.SetScopeAsync("slideshow-curation", true);
            await resumed.DecideAsync("slideshow-curation", B, "dismiss");
            QualitySuggestionSnapshot independent = await resumed.ReadAsync();
            Assert.Equal("consider-exclusion", independent.Decisions[
                QualitySuggestionWorkspace.Key("archive-cleanup", B)]);
            Assert.Equal("dismiss", independent.Decisions[
                QualitySuggestionWorkspace.Key("slideshow-curation", B)]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Refresh_preserves_choices_but_drops_photos_outside_current_candidate_population()
    {
        string dir = TempPath();
        try
        {
            QualitySuggestionWorkspace store = new(dir);
            await store.ReplaceCandidatesAsync(80, [Candidate(A, "candidate-low-sharpness", 21)]);
            await store.DecideAsync("archive-cleanup", A, "dismiss");
            await store.ReplaceCandidatesAsync(1, [Candidate(C, "candidate-low-contrast", 11)]);
            QualitySuggestionSnapshot snapshot = await store.ReadAsync();
            Assert.Single(snapshot.Candidates);
            Assert.Equal(C, snapshot.Candidates[0].RevisionId);
            Assert.Equal("dismiss", snapshot.Decisions[
                QualitySuggestionWorkspace.Key("archive-cleanup", A)]);
            Assert.Empty(QualitySuggestionWorkspace.Page(
                snapshot, "archive-cleanup", "all", "consider-exclusion", 0, 30).Items);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.DecideAsync("archive-cleanup", A, "consider-exclusion"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Invalid_review_or_scan_is_rejected_without_changing_existing_state()
    {
        string dir = TempPath();
        try
        {
            QualitySuggestionWorkspace store = new(dir);
            await store.ReplaceCandidatesAsync(5, [Candidate(A, "candidate-low-sharpness", 21)]);
            foreach (string choice in new[] { "exclude", "delete", "auto-hide" })
                await Assert.ThrowsAsync<ArgumentException>(
                    () => store.DecideAsync("archive-cleanup", A, choice));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.SetScopeAsync("unrecognized", true));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.ReplaceCandidatesAsync(3, [
                    Candidate(A, "candidate-low-sharpness", 21),
                    Candidate(A, "candidate-low-sharpness", 21)]));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.ReplaceCandidatesAsync(3, [
                    Candidate(B, "secret-new-model-classifier", 90)]));
            Assert.Single((await store.ReadAsync()).Candidates);
            Assert.Empty((await store.ReadAsync()).Decisions);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Human_review_filters_are_explicitly_bounded()
    {
        QualitySuggestionSnapshot state = QualitySuggestionSnapshot.Empty() with
        {
            Candidates =
            [
                Candidate(A, "candidate-low-sharpness", 20),
                Candidate(B, "candidate-severe-overexposure", 40),
                Candidate(C, "candidate-low-sharpness", 20)
            ],
            Decisions = new Dictionary<string, string>
            {
                [QualitySuggestionWorkspace.Key("archive-cleanup", C)] = "consider-exclusion"
            }
        };
        QualitySuggestionPage unreviewed = QualitySuggestionWorkspace.Page(
            state, "archive-cleanup", "candidate-low-sharpness", "unreviewed", 0, 30);
        Assert.Single(unreviewed.Items);
        Assert.Equal(A, unreviewed.Items[0].RevisionId);
        QualitySuggestionPage consider = QualitySuggestionWorkspace.Page(
            state, "archive-cleanup", "all", "consider-exclusion", 0, 30);
        Assert.Single(consider.Items);
        Assert.Equal(C, consider.Items[0].RevisionId);

        Assert.Throws<ArgumentException>(() => QualitySuggestionWorkspace.Page(
            state, "archive-cleanup", "all", "excluded-from-source", 0, 30));
        Assert.Throws<ArgumentException>(() => QualitySuggestionWorkspace.Page(
            state, "archive-cleanup", "all", "all", 0, 200));
    }

    private static QualitySuggestionCandidate Candidate(string id, string reason, int priority) =>
        new(id, [reason], priority);

    private static string TempPath()
    {
        string path = Path.Combine(Path.GetTempPath(), "photoidentity-manual-quality-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
