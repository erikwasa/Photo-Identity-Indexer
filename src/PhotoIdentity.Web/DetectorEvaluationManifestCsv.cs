using System.Globalization;
using System.Text;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public static class DetectorEvaluationManifestCsv
{
    public static IReadOnlyList<DetectorEvaluationManifestEntryRequest> Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        using StringReader reader = new(value);
        List<string> lines = [];
        string? headerLine = null;
        while (reader.ReadLine() is { } line)
        {
            if (headerLine is null &&
                line.Contains("Sample ID", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("Image Name", StringComparison.OrdinalIgnoreCase))
            {
                headerLine = line;
                lines.Add(line);
                continue;
            }

            if (headerLine is not null)
            {
                lines.Add(line);
            }
        }

        if (headerLine is null)
        {
            throw new FormatException("The CSV does not contain a header row with Sample ID and Image Name.");
        }

        char delimiter = CountUnquoted(headerLine, ';') > CountUnquoted(headerLine, ',') ? ';' : ',';
        IReadOnlyList<IReadOnlyList<string>> rows = ParseRows(string.Join('\n', lines), delimiter);
        if (rows.Count < 2)
        {
            throw new FormatException("The CSV contains no manifest rows.");
        }

        IReadOnlyList<string> headers = rows[0];
        Dictionary<string, int> columns = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < headers.Count; index++)
        {
            string header = headers[index].Trim().TrimStart('\uFEFF');
            if (!string.IsNullOrWhiteSpace(header))
            {
                columns.TryAdd(header, index);
            }
        }

        int sampleIdColumn = RequiredColumn(columns, "Sample ID");
        int imageNameColumn = RequiredColumn(columns, "Image Name");
        int sourceGroupColumn = RequiredColumn(columns, "Source Group");
        int categoryColumn = RequiredColumn(columns, "Primary Category");
        int countableColumn = RequiredColumn(columns, "Countable Faces");
        int? sampleGroupColumn = OptionalColumn(columns, "Sample Group");
        int? shaColumn = OptionalColumn(columns, "Source SHA-256", "Source Sha256", "SHA-256", "Sha256");

        List<DetectorEvaluationManifestEntryRequest> entries = [];
        for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            IReadOnlyList<string> row = rows[rowIndex];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            string sampleId = Cell(row, sampleIdColumn).Trim();
            string imageName = Cell(row, imageNameColumn).Trim();
            string sourceGroup = Cell(row, sourceGroupColumn).Trim();
            string primaryCategory = Cell(row, categoryColumn).Trim();
            string countableText = Cell(row, countableColumn).Trim();
            string sampleGroup = sampleGroupColumn is int sampleIndex
                ? Cell(row, sampleIndex).Trim()
                : string.Empty;
            string? sourceSha256 = shaColumn is int hashIndex
                ? NullIfWhiteSpace(Cell(row, hashIndex))
                : null;

            if (string.IsNullOrWhiteSpace(sampleId))
            {
                throw new FormatException($"Manifest row {rowIndex + 1} is missing Sample ID.");
            }

            if (string.IsNullOrWhiteSpace(imageName))
            {
                throw new FormatException($"Manifest row {rowIndex + 1} is missing Image Name.");
            }

            if (!int.TryParse(countableText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int countableFaces) ||
                countableFaces < 0)
            {
                throw new FormatException(
                    $"Manifest row {rowIndex + 1} has invalid Countable Faces value '{countableText}'.");
            }

            entries.Add(new DetectorEvaluationManifestEntryRequest(
                sampleId,
                imageName,
                sampleGroup,
                sourceGroup,
                primaryCategory,
                countableFaces,
                sourceSha256));
        }

        if (entries.Count == 0)
        {
            throw new FormatException("The CSV contains no non-empty manifest rows.");
        }

        return entries;
    }

    private static int RequiredColumn(IReadOnlyDictionary<string, int> columns, params string[] names) =>
        OptionalColumn(columns, names)
        ?? throw new FormatException($"The CSV is missing required column '{names[0]}'.");

    private static int? OptionalColumn(IReadOnlyDictionary<string, int> columns, params string[] names)
    {
        foreach (string name in names)
        {
            if (columns.TryGetValue(name, out int index))
            {
                return index;
            }
        }

        return null;
    }

    private static string Cell(IReadOnlyList<string> row, int index) =>
        index < row.Count ? row[index] : string.Empty;

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int CountUnquoted(string value, char candidate)
    {
        bool quoted = false;
        int count = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '"')
            {
                if (quoted && index + 1 < value.Length && value[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (!quoted && value[index] == candidate)
            {
                count++;
            }
        }

        return count;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseRows(string value, char delimiter)
    {
        List<IReadOnlyList<string>> rows = [];
        List<string> row = [];
        StringBuilder field = new();
        bool quoted = false;

        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < value.Length && value[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (character == '"')
            {
                quoted = true;
            }
            else if (character == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < value.Length && value[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row = [];
            }
            else
            {
                field.Append(character);
            }
        }

        if (quoted)
        {
            throw new FormatException("The CSV contains an unterminated quoted field.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }
}
