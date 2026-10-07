using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public sealed class PhotoSearchSessionState
{
    private readonly List<string> _selectedRevisionIds = [];
    private readonly HashSet<string> _selectedRevisionIdSet = new(StringComparer.OrdinalIgnoreCase);

    public string Query { get; set; } = string.Empty;

    public string Mode { get; set; } = "combined";

    public int Limit { get; set; } = 80;

    public string SmartCollectionId { get; set; } = string.Empty;

    public string CollectionName { get; set; } = string.Empty;

    public string? ExistingCollectionId { get; set; }

    public PhotoSearchResponse? Results { get; set; }

    public IReadOnlyList<string> SelectedRevisionIds => _selectedRevisionIds;

    public int SelectedCount => _selectedRevisionIds.Count;

    public bool IsSelected(string revisionId) => _selectedRevisionIdSet.Contains(revisionId);

    public void SetSelected(string revisionId, bool selected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        if (selected)
        {
            if (_selectedRevisionIdSet.Add(revisionId))
            {
                _selectedRevisionIds.Add(revisionId);
            }
            return;
        }

        if (!_selectedRevisionIdSet.Remove(revisionId))
        {
            return;
        }

        _selectedRevisionIds.RemoveAll(
            value => string.Equals(value, revisionId, StringComparison.OrdinalIgnoreCase));
    }

    public void SelectAll(IEnumerable<string> revisionIds)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        foreach (string revisionId in revisionIds)
        {
            SetSelected(revisionId, selected: true);
        }
    }

    public void ClearSelection()
    {
        _selectedRevisionIds.Clear();
        _selectedRevisionIdSet.Clear();
    }
}
