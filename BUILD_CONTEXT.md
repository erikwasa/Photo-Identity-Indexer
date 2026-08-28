# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0080 — Make the detected face unambiguous in review images** is complete.

PR #213 merged the existing-catalogue compatibility correction and exact-head workflow #1311 passed.
The maintainer then verified current `main` against the real catalogue and confirmed that WI-0080 now
works as expected. No re-analysis or face-derivative regeneration was required.

**WI-0076 — Improve archive processing throughput** remains the only in-progress engineering item.
Its session-reuse implementation is merged through PR #212 and the maintainer Scenario A/B benchmarks
passed; the registry still needs formal closeout against its acceptance criteria. **WI-0081** remains
ready and should not be started implicitly before WI-0076 is closed or deliberately deferred.

## Next concrete step

1. Review WI-0076's recorded Scenario A/B evidence against its acceptance criteria.
2. If the remaining criteria are satisfied or intentionally scoped out, record maintainer verification and complete WI-0076.
3. Close/supersede historical PR #200 if it is still open and no longer needed.
4. After WI-0076 closeout, proceed to WI-0081 if that remains the next M21 priority.

## Relevant files

- `docs/delivery/work-items/WI-0076-archive-throughput.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0080-detected-face-clarity.md`
- `docs/delivery/work-items/WI-0081-suggestion-accuracy-degradation.md`
- `BUILD_CONTEXT.md`
- `AGENTS.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
