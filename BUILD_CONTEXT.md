# Build context

## Current milestone

**M00 — Repository and architecture**

## Current work item

**WI-0002 — Create solution skeleton**

Status: `in_review`

## Branch and pull request

- Branch: `agent/WI-0002-solution-skeleton`
- Pull request: [#2 — Create .NET solution skeleton](https://github.com/erikwasa/Photo-Identity-Indexer/pull/2)

## Objective

Create the .NET 10 solution and planned project boundaries with central configuration, PowerShell build/test scripts and CI verification.

## Relevant files

- `PhotoIdentity.slnx`
- `Directory.Build.props`
- `Directory.Packages.props`
- `global.json`
- `.editorconfig`
- `.github/workflows/build.yml`
- `src/`
- `tests/`
- `docs/delivery/work-items/WI-0002-solution-skeleton.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
./build.ps1
./test.ps1
```

Equivalent direct commands:

```powershell
dotnet build PhotoIdentity.slnx
dotnet test PhotoIdentity.slnx
```

## Acceptance test

- Restore, build and test succeed using .NET 10.
- Project references follow the documented dependency direction.
- No model binaries or private photo data are tracked.

## Known issues

- Local verification is unavailable in the current agent container because the .NET SDK is not installed.
- Pull-request CI is the first executable verification of the skeleton.
- Documentation status generation remains manual until WI-0004.

## Next action

Verify the GitHub Actions build, review pull request #2, then merge and mark WI-0002 completed.
