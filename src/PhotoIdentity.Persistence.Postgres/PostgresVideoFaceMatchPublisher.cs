using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// An individual YuNet-detected video face with an in-memory SFace vector.
/// No frames, face images, file paths or embeddings are written to the catalogue.
/// </summary>
public sealed record VideoFaceEmbeddingObservation(
    int FrameOrdinal, long TimestampMs, int FaceOrdinal,
    int FrameWidth, int FrameHeight,
    double X, double Y, double Width, double Height,
    EmbeddingVector Embedding);

/// <summary>
/// Producer-authorized, exact-model SFace evidence for a current video revision.
/// The caller is responsible for guarded native frame extraction and inference.
/// </summary>
public sealed record VideoFaceEmbeddingPublication(
    AssetRevisionId RevisionId, string SourceSha256, string ProducerVersion,
    string DetectorModelId, string DetectorModelHash,
    string SFaceModelId, string SFaceModelHash, string AlignmentProtocol,
    IReadOnlyList<VideoFaceEmbeddingObservation> Observations);

/// <summary>
/// Internal bridge from already-calculated, bounded video SFace embeddings
/// through the production photo matcher into the revision-fenced evidence
/// repository. Not an API, CLI entry point, background worker or source reader.
///
/// All confirmed photo exemplars from the exact model revision participate;
/// video candidates are scored independently per face and never assigned.
/// Publication rechecks the current video source under its row lock after
/// ranking; races with exclusions, revisions and person merges fail closed.
/// </summary>
public sealed class PostgresVideoFaceMatchPublisher(
    PostgresIdentityMatchRegenerationScorer scorer,
    PostgresVideoFaceReviewRepository repository)
{
    public async Task<Guid> PublishAsync(
        VideoFaceEmbeddingPublication proposal, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.RevisionId.IsEmpty ||
            proposal.DetectorModelId != "yunet-2023mar-fp32" ||
            proposal.SFaceModelId != "sface-2021dec-fp32" ||
            proposal.AlignmentProtocol != "sface-five-point-v1" ||
            proposal.Observations is not { Count: >= 1 and <= 256 } ||
            proposal.Observations.Any(face =>
                face is null || face.Embedding is null ||
                face.Embedding.Dimensions != 128))
            throw new ArgumentException(
                "A bounded, exact-approved YuNet/SFace video face batch is required.",
                nameof(proposal));

        // Fail before any database work if the submitted geometry or
        // provenance cannot pass the eventual immutable batch checks.
        // Scores are placeholders only for the initial structural check.
        VideoFaceEvidencePublication unscored = ToEvidence(proposal,
            proposal.Observations.Select(_ =>
                (IReadOnlyList<VideoFaceIdentityScore>)Array.Empty<VideoFaceIdentityScore>()).ToArray());
        PostgresVideoFaceReviewRepository.ValidatePublication(unscored);

        var modelId = new ModelId(proposal.SFaceModelId);
        var modelHash = new Sha256Digest(proposal.SFaceModelHash);
        IReadOnlyList<IReadOnlyList<VideoFaceIdentityScore>> scores =
            await scorer.RankVideoFaceVectorsAsync(
                modelId, modelHash,
                proposal.Observations.Select(face => face.Embedding).ToArray(),
                token);
        VideoFaceEvidencePublication ranked = ToEvidence(proposal, scores);

        // The repository is the only transaction/commit boundary. The
        // approved source hash, current revision, active people and immutable
        // payload fingerprint are revalidated there after scoring.
        return await repository.PublishAsync(ranked, token);
    }

    private static VideoFaceEvidencePublication ToEvidence(
        VideoFaceEmbeddingPublication proposal,
        IReadOnlyList<IReadOnlyList<VideoFaceIdentityScore>> scores)
    {
        VideoFaceProposedObservation[] observations = proposal.Observations
            .Select((face, index) => new VideoFaceProposedObservation(
                face.FrameOrdinal, face.TimestampMs, face.FaceOrdinal,
                face.FrameWidth, face.FrameHeight,
                face.X, face.Y, face.Width, face.Height,
                scores[index].Select(lead => new VideoFaceRankedPerson(
                    lead.PersonId, Math.Clamp(lead.Similarity, -1, 1))).ToArray()))
            .ToArray();
        return new VideoFaceEvidencePublication(
            proposal.RevisionId, proposal.SourceSha256, proposal.ProducerVersion,
            proposal.DetectorModelId, proposal.DetectorModelHash,
            proposal.SFaceModelId, proposal.SFaceModelHash,
            proposal.AlignmentProtocol, observations);
    }
}
