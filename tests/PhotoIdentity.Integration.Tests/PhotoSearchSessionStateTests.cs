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
    public void Smart_collection_scope_is_part_of_the_persistent_search_state()
    {
        PhotoSearchSessionState state = new()
        {
            Query = "playing by water",
            SmartCollectionId = "00000000-0000-0000-0000-000000000186",
        };

        state.SelectAll(["revision-1"]);
        state.Query = "swimming";

        Assert.Equal("00000000-0000-0000-0000-000000000186", state.SmartCollectionId);
        Assert.Equal("swimming", state.Query);
        Assert.Equal(["revision-1"], state.SelectedRevisionIds);
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
