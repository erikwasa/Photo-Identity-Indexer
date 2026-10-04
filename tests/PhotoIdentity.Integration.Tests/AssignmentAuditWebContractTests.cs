using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class AssignmentAuditWebContractTests
{
    [Fact]
    public void Audit_page_uses_cross_person_filters_grouping_lazy_images_and_return_context()
    {
        string root = ResolveRepositoryRoot();
        string page = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PhotoIdentity.Web",
            "Pages",
            "PersonAudit.razor"));

        Assert.Contains("<h1>Audit assignments</h1>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Choose person", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"audit-source\"", page, StringComparison.Ordinal);
        Assert.Contains("type=\"datetime-local\"", page, StringComparison.Ordinal);
        Assert.Contains("Items.GroupBy(item => item.AssignedPerson.Id)", page, StringComparison.Ordinal);
        Assert.Contains("face.AcceptedSuggestion", page, StringComparison.Ordinal);
        Assert.Contains("loading=\"lazy\"", page, StringComparison.Ordinal);
        Assert.Contains("Load more", page, StringComparison.Ordinal);
        Assert.Contains("visible=", page, StringComparison.Ordinal);
        Assert.Contains("#\{PersonAnchor(face.AssignedPerson.Id)\}", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Audit_styles_keep_filters_and_person_context_sticky_without_forcing_phone_overflow()
    {
        string root = ResolveRepositoryRoot();
        string css = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PhotoIdentity.Web",
            "Pages",
            "PersonAudit.razor.css"));

        Assert.Contains(".audit-controls {", css, StringComparison.Ordinal);
        Assert.Contains("position: sticky;", css, StringComparison.Ordinal);
        Assert.Contains(".audit-person-header {", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 760px)", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1fr;", css, StringComparison.Ordinal);
    }

    private static string ResolveRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PhotoIdentity.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not resolve Photo Identity repository root.");
    }
}
