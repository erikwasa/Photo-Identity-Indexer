using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Source-level guard for the opt-in UI/API boundary. No source media,
/// recognition model, OneDrive, provider or catalogue is opened here.
/// </summary>
public sealed class VideoDetailsLayoutContractTests
{
    [Fact]
    public void Video_details_remain_explicit_and_distinct_from_photo_or_recognition()
    {
        string root = Root();
        string listing = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "Pages", "Videos.razor"));
        string details = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "Pages", "VideoDetails.razor"));
        string api = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Api", "VideoDetailsEndpoints.cs"));
        string repository = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Persistence.Postgres", "PostgresVideoDetailsRepository.cs"));
        string schema = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Persistence.Postgres", "PostgresCatalogueDatabase.VideoDetailsSchema.cs"));

        Assert.Contains("Video details &amp; people", listing);
        Assert.Contains("@page \"/videos/{RevisionId}\"", details);
        Assert.Contains("Manual presence means", details);
        Assert.Contains("Look up Place from GPS", details);
        Assert.Contains("Restore previous GPS Place", details);
        Assert.Contains("type=\"text\" inputmode=\"numeric\" maxlength=\"16\"", details);
        Assert.Contains("placeholder=\"YYYY-MM-DD HH:MM\"", details);
        Assert.Contains("VideoDetailsPresentation.TryParseLocalRecordingTime", details);
        Assert.Contains("VideoDetailsPresentation.FormatFrameRate", details);
        Assert.Contains("VideoDetailsPresentation.FormatVideoBitrate", details);
        Assert.Contains("VideoDetailsPresentation.FormatAudioDetails", details);
        Assert.Contains("m.video_bitrate_bps", repository);
        Assert.Contains("m.audio_sample_rate_hz", repository);
        Assert.Contains("m.audio_channels", repository);
        Assert.DoesNotContain("type=\"datetime-local\"", details);
        Assert.Contains("TimeZoneInfo.Local", details);
        Assert.Contains("Original recording time (local)", details);
        Assert.Contains("FormatFrameRate", details);
        Assert.Contains("FormatBitDepth", details);
        Assert.Contains("FormatVideoCodec", details);
        Assert.Contains("It does not detect a face", details);
        Assert.Contains("/api/videos/{revisionId}/resolve-place", api);
        Assert.Contains("/api/videos/{revisionId}/restore-gps-place", api);
        Assert.Contains("RestoreGpsPlaceAsync", repository);
        Assert.Contains("GetCachedAsync", api);
        Assert.Contains("ReverseGeocodeAsync", api);
        Assert.Contains("ManualPlaceOverride", api);
        Assert.Contains("CurrentVideoPredicate", repository);
        Assert.Contains("FOR UPDATE OF source", repository);
        Assert.Contains("source_copy_exclusions", repository);
        Assert.Contains("video_person_actions", repository);
        Assert.Contains("video_capture_date_actions", schema);
        Assert.Contains("video_place_actions", schema);
        Assert.Contains("trg_source_exclusion_purge_video_details", schema);
        Assert.DoesNotContain("face_occurrences", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identity_suggestions", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffmpeg", details, StringComparison.OrdinalIgnoreCase);
        // Comments may describe OneDrive as an explicitly excluded dependency.
        // Keep the boundary guard on implementation lines, not documentation.
        string implementation = string.Join('\n', repository.Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.DoesNotContain("OneDrive", implementation, StringComparison.OrdinalIgnoreCase);
    }

    private static string Root()
    {
        foreach (string initial in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? current = new(Path.GetFullPath(initial));
            for (int depth = 0; current is not null && depth < 12; depth++, current = current.Parent)
                if (File.Exists(Path.Combine(current.FullName, "PhotoIdentity.slnx")))
                    return current.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
