# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0095 — Retry transient governed-model download interruptions** is implemented and in review on PR #226.

Main workflow #1356 failed only in Windows package verification while installing governed ONNX model files:

    error: The response ended prematurely. (ResponseEnded)

The ModelInstaller correction retries only transient HTTP/stream failures up to three attempts. Integrity mismatch remains non-retryable, partial temporary files are deleted before retry, valid existing models still perform no network request, and cancellation is not converted into retry.

WI-0096 / PR #227 is merged. Its PR workflow #1361 and post-merge main workflow #1362 are green, confirming preparation cancellation now quiesces its background RunTask before catalogue teardown.

PR #226 was rebuilt on the WI-0096 merge commit. Exact-head workflow #1363 passed build-and-test, both integration shards, launcher verification and package verification together. This simultaneously confirms the original package-download failure is corrected and the previous WI-0093 SQLite teardown failure no longer reproduces.

WI-0082 through WI-0086 and WI-0092 through WI-0096 remain in review pending the final corrective lifecycle gate and consolidated real-phone M22 acceptance.

WI-0076 remains separately recorded as in_progress and is not part of this M22 slice.

## Next concrete step

1. Wait for lifecycle-only CI on the current PR #226 head.
2. If green and no review blockers exist, merge PR #226.
3. Require the post-merge main package verification to pass.
4. Then perform consolidated real-phone M22 acceptance.

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
