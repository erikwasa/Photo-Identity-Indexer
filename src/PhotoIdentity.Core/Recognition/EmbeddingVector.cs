namespace PhotoIdentity.Core.Recognition;

public sealed class EmbeddingVector
{
    private readonly float[] _values;

    public EmbeddingVector(ReadOnlySpan<float> values)
    {
        if (values.IsEmpty)
        {
            throw new ArgumentException("Embedding must contain at least one component.", nameof(values));
        }

        _values = values.ToArray();
        double normSquared = 0;

        foreach (float value in _values)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException("Embedding components must be finite.", nameof(values));
            }

            normSquared += value * value;
        }

        if (normSquared == 0)
        {
            throw new ArgumentException("Embedding cannot be the zero vector.", nameof(values));
        }

        L2Norm = Math.Sqrt(normSquared);
    }

    public int Dimensions => _values.Length;
    public double L2Norm { get; }
    public ReadOnlySpan<float> Values => _values;

    public EmbeddingVector Normalize()
    {
        float[] normalised = new float[_values.Length];
        for (int index = 0; index < _values.Length; index++)
        {
            normalised[index] = (float)(_values[index] / L2Norm);
        }

        return new EmbeddingVector(normalised);
    }

    public double CosineSimilarity(EmbeddingVector other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Dimensions != other.Dimensions)
        {
            throw new ArgumentException("Embedding dimensions must match.", nameof(other));
        }

        double dotProduct = 0;
        for (int index = 0; index < _values.Length; index++)
        {
            dotProduct += _values[index] * other._values[index];
        }

        return dotProduct / (L2Norm * other.L2Norm);
    }

    public float[] ToArray() => (float[])_values.Clone();
}
