using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveCommandTests
{
    [Fact]
    public async Task Include_list_and_sync_persist_and_expand_archive_coverage()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string archiveRoot = Path.Combine(directory, "Kamerabilder");
            string january = Path.Combine(archiveRoot, "1970", "01");
            string february = Path.Combine(archiveRoot, "1970", "02");
            string march = Path.Combine(archiveRoot, "1970", "03");
            Directory.CreateDirectory(january);
            Directory.CreateDirectory(february);
            Directory.CreateDirectory(march);
            await File.WriteAllBytesAsync(Path.Combine(january, "a.jpg"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(february, "b.jpg"), [2]);
            await File.WriteAllBytesAsync(Path.Combine(march, "c.jpg"), [3]);

            string databasePath = Path.Combine(directory, "catalogue.db");

            Assert.Equal(0, await RunAsync(
                ["archive", "include", "--database", databasePath, "--root", archiveRoot, "--folder", "1970/01"]));
            Assert.Equal(0, await RunAsync(
                ["archive", "include", "--database", databasePath, "--root", archiveRoot, "--folder", "1970/02"]));

            (int listExit, string listOutput, string listError) = await RunWithOutputAsync(
                ["archive", "list", "--database", databasePath]);
            Assert.Equal(0, listExit);
            Assert.Equal(string.Empty, listError);
            Assert.Contains("included-folders: 2", listOutput, StringComparison.Ordinal);
            Assert.Contains("included: 1970/01", listOutput, StringComparison.Ordinal);
            Assert.Contains("included: 1970/02", listOutput, StringComparison.Ordinal);

            Assert.Equal(0, await RunAsync(
                ["archive", "include", "--database", databasePath, "--root", archiveRoot, "--folder", "1970"]));

            (int collapsedExit, string collapsedOutput, _) = await RunWithOutputAsync(
                ["archive", "list", "--database", databasePath]);
            Assert.Equal(0, collapsedExit);
            Assert.Contains("included-folders: 1", collapsedOutput, StringComparison.Ordinal);
            Assert.Contains("included: 1970", collapsedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("included: 1970/01", collapsedOutput, StringComparison.Ordinal);

            (int firstSyncExit, string firstSyncOutput, string firstSyncError) = await RunWithOutputAsync(
                ["archive", "sync", "--database", databasePath]);
            Assert.Equal(0, firstSyncExit);
            Assert.Equal(string.Empty, firstSyncError);
            Assert.Contains("scan-supported: 3", firstSyncOutput, StringComparison.Ordinal);
            Assert.Contains("scan-new-revisions: 3", firstSyncOutput, StringComparison.Ordinal);

            await File.WriteAllBytesAsync(Path.Combine(january, "new.jpg"), [4]);
            (int secondSyncExit, string secondSyncOutput, _) = await RunWithOutputAsync(
                ["archive", "sync", "--database", databasePath]);
            Assert.Equal(0, secondSyncExit);
            Assert.Contains("scan-supported: 4", secondSyncOutput, StringComparison.Ordinal);
            Assert.Contains("scan-new-revisions: 1", secondSyncOutput, StringComparison.Ordinal);
            Assert.Contains("scan-unchanged: 3", secondSyncOutput, StringComparison.Ordinal);

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            ArchiveCoverageConfiguration configured = Assert.IsType<ArchiveCoverageConfiguration>(
                await new SqliteArchiveCoverageRepository(database).GetAsync());
            Assert.Equal(Path.GetFullPath(archiveRoot), configured.Source.RootLocator);
            Assert.Equal(["1970"], configured.IncludedFolders);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Inventory_reports_extension_families_without_private_paths_or_file_names()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string archiveRoot = Path.Combine(directory, "Kamerabilder");
            string included = Path.Combine(archiveRoot, "2026", "08");
            Directory.CreateDirectory(included);
            await File.WriteAllBytesAsync(Path.Combine(included, "phone.heic"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(included, "camera.dng"), [2]);
            await File.WriteAllBytesAsync(Path.Combine(included, "existing.jpg"), [3]);
            await File.WriteAllBytesAsync(Path.Combine(included, "notes.txt"), [4]);

            string databasePath = Path.Combine(directory, "catalogue.db");
            Assert.Equal(0, await RunAsync(
                ["archive", "include", "--database", databasePath, "--root", archiveRoot, "--folder", "2026/08"]));

            (int exitCode, string output, string error) = await RunWithOutputAsync(
                ["archive", "inventory", "--database", databasePath]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error);
            Assert.Contains("archive-media-inventory: complete", output, StringComparison.Ordinal);
            Assert.Contains("files-total: 4", output, StringComparison.Ordinal);
            Assert.Contains("extension: .heic count=1 family=heif supported=true", output, StringComparison.Ordinal);
            Assert.Contains("extension: .dng count=1 family=raw supported=false", output, StringComparison.Ordinal);
            Assert.Contains("extension: .jpg count=1 family=jpeg supported=true", output, StringComparison.Ordinal);
            Assert.Contains("extension: .txt count=1 family=other supported=false", output, StringComparison.Ordinal);
            Assert.DoesNotContain("phone.heic", output, StringComparison.Ordinal);
            Assert.DoesNotContain("camera.dng", output, StringComparison.Ordinal);
            Assert.DoesNotContain(archiveRoot, output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Include_rejects_a_different_archive_root_after_configuration()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string firstRoot = Path.Combine(directory, "first");
            string secondRoot = Path.Combine(directory, "second");
            Directory.CreateDirectory(Path.Combine(firstRoot, "1970"));
            Directory.CreateDirectory(Path.Combine(secondRoot, "1970"));
            string databasePath = Path.Combine(directory, "catalogue.db");

            Assert.Equal(0, await RunAsync(
                ["archive", "include", "--database", databasePath, "--root", firstRoot, "--folder", "1970"]));

            (int exitCode, _, string error) = await RunWithOutputAsync(
                ["archive", "include", "--database", databasePath, "--root", secondRoot, "--folder", "1970"]);

            Assert.Equal(2, exitCode);
            Assert.Contains("already configured for archive root", error, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        (int exitCode, _, string error) = await RunWithOutputAsync(args);
        Assert.Equal(string.Empty, error);
        return exitCode;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunWithOutputAsync(string[] args)
    {
        StringWriter output = new();
        StringWriter error = new();
        int exitCode = await Program.RunAsync(args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
