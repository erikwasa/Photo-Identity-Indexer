using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class DetectorRolloutTests
{
    [Fact]
    public void Pipeline_hash_is_stable_and_changes_for_material_detector_behaviour()
    {
        DetectorPipelineDefinition baseline = Pipeline(confidence: 0.5, maximumLongEdge: 1600);

        Assert.Equal(baseline.ComputeHash(), Pipeline(confidence: 0.5, maximumLongEdge: 1600).ComputeHash());
        Assert.NotEqual(baseline.ComputeHash(), Pipeline(confidence: 0.6, maximumLongEdge: 1600).ComputeHash());
        Assert.NotEqual(baseline.ComputeHash(), Pipeline(confidence: 0.5, maximumLongEdge: 1280).ComputeHash());
        Assert.NotEqual(
            baseline.ComputeHash(),
            Pipeline(confidence: 0.5, maximumLongEdge: 1600, detectorNmsThreshold: 0.4).ComputeHash());
    }

    [Fact]
    public void Reconciliation_matches_by_geometry_and_landmarks_not_candidate_order()
    {
        ExistingFaceDetectionAnchor first = Existing(
            new NormalizedBoundingBox(0.08, 0.12, 0.24, 0.32));
        ExistingFaceDetectionAnchor second = Existing(
            new NormalizedBoundingBox(0.62, 0.15, 0.22, 0.30));

        CandidateFaceDetectionAnchor[] candidates =
        [
            Candidate(0, new NormalizedBoundingBox(0.615, 0.145, 0.23, 0.31)),
            Candidate(1, new NormalizedBoundingBox(0.075, 0.115, 0.25, 0.33)),
        ];

        FaceDetectionReconciliationPlan plan = FaceDetectionReconciliationPlanner.Plan(
            [first, second],
            candidates);

        Assert.False(plan.HasAmbiguity);
        Assert.Equal(second.FaceOccurrenceId, plan.CandidateDecisions[0].ExistingFaceOccurrenceId);
        Assert.Equal(first.FaceOccurrenceId, plan.CandidateDecisions[1].ExistingFaceOccurrenceId);
        Assert.Empty(plan.ExistingOccurrencesWithoutCandidate);
    }

    [Fact]
    public void Reconciliation_marks_new_faces_without_consuming_old_occurrences()
    {
        ExistingFaceDetectionAnchor existing = Existing(
            new NormalizedBoundingBox(0.10, 0.10, 0.24, 0.32));
        CandidateFaceDetectionAnchor added = Candidate(
            0,
            new NormalizedBoundingBox(0.68, 0.55, 0.16, 0.22));

        FaceDetectionReconciliationPlan plan = FaceDetectionReconciliationPlanner.Plan(
            [existing],
            [added]);

        FaceDetectionReconciliationDecision decision = Assert.Single(plan.CandidateDecisions);
        Assert.Equal(FaceDetectionReconciliationDisposition.NewOccurrence, decision.Disposition);
        Assert.Null(decision.ExistingFaceOccurrenceId);
        Assert.Equal(existing.FaceOccurrenceId, Assert.Single(plan.ExistingOccurrencesWithoutCandidate));
    }

    [Fact]
    public void Reconciliation_refuses_ambiguous_geometry()
    {
        ExistingFaceDetectionAnchor left = Existing(
            new NormalizedBoundingBox(0.30, 0.25, 0.30, 0.40));
        ExistingFaceDetectionAnchor right = Existing(
            new NormalizedBoundingBox(0.34, 0.25, 0.30, 0.40));
        CandidateFaceDetectionAnchor candidate = Candidate(
            0,
            new NormalizedBoundingBox(0.32, 0.25, 0.30, 0.40));

        FaceDetectionReconciliationPlan plan = FaceDetectionReconciliationPlanner.Plan(
            [left, right],
            [candidate],
            new FaceDetectionReconciliationOptions
            {
                MinimumIntersectionOverUnion = 0.30,
                MaximumLandmarkDistanceRatio = 0.30,
            });

        FaceDetectionReconciliationDecision decision = Assert.Single(plan.CandidateDecisions);
        Assert.True(plan.HasAmbiguity);
        Assert.Equal(FaceDetectionReconciliationDisposition.Ambiguous, decision.Disposition);
        Assert.Null(decision.ExistingFaceOccurrenceId);
        Assert.Equal(2, decision.PossibleExistingFaceOccurrenceIds.Count);
        Assert.Equal(2, plan.ExistingOccurrencesWithoutCandidate.Count);
    }

    private static DetectorPipelineDefinition Pipeline(
        double confidence,
        int maximumLongEdge,
        double detectorNmsThreshold = 0.3) => new(
            implementationId: "centerface-opencv-dnn-v1",
            detectorModelId: new ModelId("centerface-2019-fp32"),
            detectorModelHash: new Sha256Digest(new string('a', 64)),
            runtime: "opencv-dnn",
            confidenceThreshold: confidence,
            pipelineMode: "single-pass",
            resizePolicy: "direct-resize-bounded-dynamic-multiple-of",
            inputWidth: 640,
            inputHeight: 640,
            inputShapePolicy: "dynamic-multiple-of",
            inputMultipleOf: 32,
            maximumLongEdge: maximumLongEdge,
            colourOrder: "RGB",
            dataType: "float32",
            inputScale: 1.0,
            inputMean: [0, 0, 0],
            detectorNmsThreshold: detectorNmsThreshold,
            detectorTopK: 5000,
            tileSize: null,
            tileOverlap: null,
            mergeNmsThreshold: null,
            rotationPolicy: "none");

    private static ExistingFaceDetectionAnchor Existing(NormalizedBoundingBox box) => new(
        FaceOccurrenceId.New(),
        box,
        Landmarks(box));

    private static CandidateFaceDetectionAnchor Candidate(int index, NormalizedBoundingBox box) => new(
        index,
        box,
        Landmarks(box));

    private static NormalizedFaceLandmarks Landmarks(NormalizedBoundingBox box)
    {
        NormalizedPoint Point(double xRatio, double yRatio) => new(
            box.X + (box.Width * xRatio),
            box.Y + (box.Height * yRatio));

        return new NormalizedFaceLandmarks(
            LeftEye: Point(0.68, 0.34),
            RightEye: Point(0.32, 0.34),
            Nose: Point(0.50, 0.52),
            MouthLeft: Point(0.65, 0.72),
            MouthRight: Point(0.35, 0.72));
    }
}
