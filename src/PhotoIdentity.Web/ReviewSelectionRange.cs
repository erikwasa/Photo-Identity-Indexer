using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public static class ReviewSelectionRange
{
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<ReviewFaceResponse> faces,
        string anchorFaceId,
        string targetFaceId)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentException.ThrowIfNullOrWhiteSpace(anchorFaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFaceId);

        int anchorIndex = FindIndex(faces, anchorFaceId);
        int targetIndex = FindIndex(faces, targetFaceId);
        if (anchorIndex < 0 || targetIndex < 0)
        {
            return [];
        }

        int start = Math.Min(anchorIndex, targetIndex);
        int end = Math.Max(anchorIndex, targetIndex);
        return faces
            .Skip(start)
            .Take(end - start + 1)
            .Where(face => string.Equals(face.State, "unreviewed", StringComparison.Ordinal))
            .Select(face => face.Id)
            .ToArray();
    }

    private static int FindIndex(IReadOnlyList<ReviewFaceResponse> faces, string id)
    {
        for (int index = 0; index < faces.Count; index++)
        {
            if (string.Equals(faces[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
