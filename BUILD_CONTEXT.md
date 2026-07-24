# Build context

## Current milestone

**M00 — Repository and architecture**

## Current work item

**WI-0003 — Define core identifiers and contracts**

Status: `in_review`

## Branch and pull request

- Branch: `agent/WI-0003-core-contracts`
- Pull request: [#3 — Define core identifiers and contracts](https://github.com/erikwasa/Photo-Identity-Indexer/pull/3)

## Objective

Define the neutral domain values and ports used by replaceable source, image, recognition and persistence adapters.

## Relevant files

- `src/PhotoIdentity.Core/Identifiers/`
- `src/PhotoIdentity.Core/Geometry/`
- `src/PhotoIdentity.Core/Imaging/`
- `src/PhotoIdentity.Core/Recognition/`
- `src/PhotoIdentity.Core/Sources/`
- `src/PhotoIdentity.Core/README.md`
- `tests/PhotoIdentity.Core.Tests/`
- `docs/delivery/work-items/WI-0003-core-types.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
./build.ps1
./test.ps1
```

Focused command:

```powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj
```

## Acceptance test

- Strong identifiers are type-distinct and validated.
- Pixel and normalised coordinate spaces cannot be confused accidentally.
- Bounding-box IoU and vector similarity are tested.
- Core exposes no infrastructure-specific dependencies or types.

## Verification

- GitHub Actions run `30130764843` successfully restored, built and tested the solution on Windows with .NET 10.
- The current agent container does not contain the .NET SDK, so no independent local build was run.
- Documentation status generation remains manual until WI-0004.

## Next action

Review and merge pull request #3, then mark WI-0003 completed and make WI-0004 ready.
