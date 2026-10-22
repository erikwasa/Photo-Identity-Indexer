using System.Security.AccessControl;
using System.Text.Json;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoAppContainerLabTests
{
    private static string NewLab()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "wi0188-appcontainer-lab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,
            VideoSyntheticProxyPilotCommandRunner.RootMarker),
            VideoSyntheticProxyPilotCommandRunner.MarkerText);
        return root;
    }

    [Fact]
    public void Parser_refuses_media_tools_archive_paths_and_nonlocal_apply()
    {
        string root = Path.Combine(Path.GetTempPath(), "appcontainer-options");
        var options = VideoAppContainerLabOptions.Parse(
            ["--scratch-root", root]);
        Assert.False(options.Apply);
        Assert.True(VideoAppContainerLabOptions.Parse(
            ["--scratch-root", root, "--apply"]).Apply);

        foreach (string[] bad in new[]
        {
            Array.Empty<string>(),
            new[] { "--scratch-root", "relative" },
            new[] { "--scratch-root", root, "--scratch-root", root },
            new[] { "--scratch-root", root, "--apply", "--apply" },
            new[] { "--scratch-root", root, "--source-path", @"C:\\Archive\\original.mov" },
            new[] { "--scratch-root", root, "--revision-id", Guid.NewGuid().ToString() },
            new[] { "--scratch-root", root, "--ffmpeg", @"C:\\tools\\ffmpeg.exe" },
            new[] { "--scratch-root", root, "--network", "on" },
            new[] { "--scratch-root", root, "--job-id", Guid.NewGuid().ToString() }
        })
            Assert.Throws<ArgumentException>(() =>
                VideoAppContainerLabOptions.Parse(bad));
    }

    [Fact]
    public async Task Preview_never_creates_profile_or_changes_marked_root()
    {
        string root = NewLab();
        try
        {
            using StringWriter output = new();
            var options = VideoAppContainerLabOptions.Parse(
                ["--scratch-root", root]);
            Assert.Equal(0, await VideoAppContainerLabCommandRunner.RunAsync(
                options, output));
            using JsonDocument doc = JsonDocument.Parse(output.ToString());
            JsonElement result = doc.RootElement;
            Assert.True(result.GetProperty("dryRun").GetBoolean());
            Assert.Equal("not-run", result.GetProperty("status").GetString());
            Assert.False(result.GetProperty("appContainerTokenVerified").GetBoolean());
            Assert.False(result.GetProperty("syntheticAclAuditVerified").GetBoolean());
            Assert.False(result.GetProperty("networkCapabilitiesRequested").GetBoolean());
            Assert.False(result.GetProperty("realVideoExecutionAuthorized").GetBoolean());
            Assert.Single(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Lab_refuses_unmarked_or_occupied_roots_before_any_profile_creation()
    {
        string root = NewLab();
        try
        {
            File.WriteAllText(Path.Combine(root, "an-unrelated-original.mov"),
                "unexpected bytes");
            using StringWriter output = new();
            Assert.Throws<InvalidOperationException>(() =>
                VideoAppContainerLabCommandRunner.RunAsync(
                    VideoAppContainerLabOptions.Parse(
                        ["--scratch-root", root, "--apply"]), output)
                    .GetAwaiter().GetResult());
            Assert.True(File.Exists(
                Path.Combine(root, "an-unrelated-original.mov")));
            Assert.Equal("", output.ToString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void AppContainer_profile_factory_rejects_unmarked_existing_folders()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(),
            "wi0188-unmarked-acl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<ArgumentException>(() =>
                PhotoIdentity.Core.Processing.WindowsVideoAppContainerProfile
                    .CreateSyntheticLab(root));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root); }
    }

    [Fact]
    public void Sanitized_report_cannot_claim_full_network_or_quota_isolation()
    {
        using StringWriter result = new();
        VideoAppContainerLabCommandRunner.Write(
            result, false, "file-boundary-observed", true, true, true, true,
            syntheticAclAudit: true);
        using JsonDocument json = JsonDocument.Parse(result.ToString());
        JsonElement report = json.RootElement;
        Assert.Equal("wi0188-synthetic-appcontainer-feasibility-v1",
            report.GetProperty("protocol").GetString());
        Assert.True(report.GetProperty("appContainerTokenVerified").GetBoolean());
        Assert.True(report.GetProperty("allowedReadWriteVerified").GetBoolean());
        Assert.True(report.GetProperty("ungrantedReadDenied").GetBoolean());
        Assert.True(report.GetProperty("profileAndScratchCleanupVerified").GetBoolean());
        Assert.True(report.GetProperty("syntheticAclAuditVerified").GetBoolean());
        foreach (string field in new[]
        {
            "networkCapabilitiesRequested", "networkEgressDenialVerified",
            "independentAclAuditVerified", "hardDiskIoQuotaVerified",
            "originalsOpened", "oneDriveHydrationRequested",
            "jobsEnqueued", "derivativesPublished", "realVideoExecutionAuthorized"
        })
            Assert.False(report.GetProperty(field).GetBoolean());
        foreach (string privateField in new[]
        {
            "username", "sourcePath", "password", "profileName", "sid",
            "fileName", "archive", "sourceSha256", "userProfile"
        })
            Assert.DoesNotContain(privateField, result.ToString(),
                StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() =>
            VideoAppContainerLabCommandRunner.Write(
                result, false, @"C:\\Archive\\private.mov", false,
                false, false, false));
    }


    [Fact]
    public void Nonpassing_acl_audit_report_never_claims_full_sandbox()
    {
        using StringWriter output = new();
        VideoAppContainerLabCommandRunner.Write(output, false,
            "sandbox-acl-audit-failed", false, false, false, true,
            syntheticAclAudit: false);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement report = json.RootElement;
        Assert.False(report.GetProperty("syntheticAclAuditVerified").GetBoolean());
        Assert.False(report.GetProperty("networkEgressDenialVerified").GetBoolean());
        Assert.False(report.GetProperty("independentAclAuditVerified").GetBoolean());
        Assert.False(report.GetProperty("realVideoExecutionAuthorized").GetBoolean());
    }

    [Fact]
    public async Task Windows_appcontainer_lab_never_falls_back_to_full_token_or_leaks_scratch()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = NewLab();
        try
        {
            string beforeAcl = new DirectoryInfo(root)
                .GetAccessControl(AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            using StringWriter output = new();
            int exit = await VideoAppContainerLabCommandRunner.RunAsync(
                VideoAppContainerLabOptions.Parse(
                    ["--scratch-root", root, "--apply"]), output);
            using JsonDocument json = JsonDocument.Parse(output.ToString());
            JsonElement report = json.RootElement;
            string status = report.GetProperty("status").GetString()!;
            bool token = report.GetProperty("appContainerTokenVerified").GetBoolean();
            bool granted = report.GetProperty("allowedReadWriteVerified").GetBoolean();
            bool denied = report.GetProperty("ungrantedReadDenied").GetBoolean();
            bool cleaned = report.GetProperty(
                "profileAndScratchCleanupVerified").GetBoolean();
            bool audit = report.GetProperty("syntheticAclAuditVerified").GetBoolean();
            Assert.True(cleaned, "Windows AppContainer and owned lab must clean up.");
            Assert.Single(Directory.EnumerateFileSystemEntries(root));
            string afterAcl = new DirectoryInfo(root)
                .GetAccessControl(AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            Assert.Equal(beforeAcl, afterAcl);
            Assert.False(report.GetProperty("networkEgressDenialVerified").GetBoolean());
            Assert.False(report.GetProperty("realVideoExecutionAuthorized").GetBoolean());
            if (status == "file-boundary-observed")
            {
                Assert.Equal(0, exit);
                Assert.True(token && granted && denied && audit);
            }
            else
            {
                // Windows runners can reject AppContainer creation, startup
                // or directory ACLs. That is an UNSUPPORTED experiment, not
                // positive security evidence or permission to process media.
                Assert.Equal(1, exit);
                Assert.Contains(status, new[]
                {
                    "profile-unavailable", "sandbox-acl-unavailable",
                    "sandbox-launch-unavailable", "allowed-read-write-unavailable",
                    "sandbox-acl-audit-failed", "ungranted-read-not-denied"
                });
                Assert.False(token && granted && denied);
            }
            Assert.DoesNotContain(root, output.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
