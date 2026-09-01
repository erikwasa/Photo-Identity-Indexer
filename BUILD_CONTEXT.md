# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0095 — Retry transient governed-model download interruptions** is the active corrective M22 item on PR #226, rebased onto main after WI-0096 merged.

Main workflow #1356 failed only in Windows package verification while installing governed ONNX model files:

    error: The response ended prematurely. (ResponseEnded)

The ModelInstaller correction retries only transient HTTP/stream failures up to three attempts. Integrity mismatch remains non-retryable, partial temporary files are deleted before retry, valid existing models still perform no network request, and cancellation is not converted into retry.

Earlier workflow #1360 demonstrated that package verification passes with this correction. That run also exposed a separate deterministic WI-0093 preparation teardown race; WI-0096 corrected it by awaiting preparation background-task quiescence. PR #227 / workflow #1361 are green and merged, and WI-0096 is now recorded in_review.

PR #226 has been rebuilt directly on the WI-0096 merge commit so final CI validates both corrections together without overwriting WI-0096 lifecycle/docs changes.

WI-0082 through WI-0086 and WI-0092 through WI-0096 remain in review/in progress as appropriate pending the remaining corrective gate and consolidated real-phone M22 acceptance.

WI-0076 remains separately recorded as in_progress and is not part of this M22 slice.

## Next concrete step

1. Run exact-head CI for rebased PR #226.
2. Require build-and-test, both integration shards, launcher verification and package verification to pass together.
3. If green, record evidence and move WI-0095 to in_review.
4. Merge PR #226 and require the post-merge main package verification to pass.
5. Then perform consolidated real-phone M22 acceptance.

## Relevant files

- docs/delivery/work-items/WI-0095-model-download-retry.md
- src/PhotoIdentity.Recognition.Onnx/Models/ModelInstaller.cs
- tests/PhotoIdentity.Recognition.Tests/ModelInstallerTests.cs
- docs/delivery/work-items/WI-0096-slideshow-preparation-quiescence.md
- src/PhotoIdentity.Api/SlideshowOriginalPreparationService.cs
- src/PhotoIdentity.Api/SlideshowOriginalPreparationEndpoints.cs
- docs/delivery/status/work-items.yaml
- docs/delivery/status/milestones.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
