using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoSearchSessionStateTests
{
    [Fact]
    public void Selection_accumulates_across_searches_in_first_seen_order()
    {
        PhotoSearchSessionState state = new();

        state.SelectAll(["revision-1", "revision-2"]);
        state.SelectAll(["revision-2", "revision-3"]);

        Assert.Equal(3, state.SelectedCount);
        Assert.Equal(
            ["revision-1", "revision-2", "revision-3"],
            state.SelectedRevisionIds);
    }

    [Fact]
    public void Deselecting_removes_only_the_requested_revision()
    {
        PhotoSearchSessionState state = new();
        state.SelectAll(["revision-1", "revision-2", "revision-3"]);

        state.SetSelected("REVISION-2", selected: false);

        Assert.False(state.IsSelected("revision-2"));
        Assert.Equal(["revision-1", "revision-3"], state.SelectedRevisionIds);
    }

    [Fact]
    public void Clear_selection_resets_the_working_collection()
    {
        PhotoSearchSessionState state = new();
        state.SelectAll(["revision-1", "revision-2"]);

        state.ClearSelection();

        Assert.Equal(0, state.SelectedCount);
        Assert.Empty(state.SelectedRevisionIds);
    }
}
