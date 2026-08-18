# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** has completed its implementation slices. PR #179 added the path-aware deployment gate and reduced PR published-runtime smoke; PR #180 then proved the unrelated-path branch with workflow #1134 at about 3m37s required Windows critical path and about 9.9 Windows runner-minutes. Final-head workflow #1136 was also green. WI-0070 remains open only because its acceptance wording asks for three representative successful PR samples rather than one independently measured PR.

**WI-0071 — Stabilize quarantined API integration tests** is the next implementation focus while that natural WI-0070 evidence accumulates. Four transient-500 endpoint tests remain in `.github/flaky-integration-tests.txt`; they execute once per workflow in the visible non-blocking diagnostic lane with no retries.

Repository inspection shows that all four quarantined classes still use bare per-class `WebApplicationFactory` implementations that only set the catalogue database path. The first WI-0071 slice migrates only `ReviewProgressFilterApplicationTests` to `PhotoIdentityApiTestFactory` and improves expected-status response diagnostics. Its quarantine entry must remain until three consecutive representative post-change diagnostic runs pass.

M19 feature work remains separately in review/in progress and must be preserved when branches are synchronized with `main`.

## Next concrete step

1. Validate the first WI-0071 shared-host migration in GitHub Actions without adding retries or removing quarantine.
2. Treat that PR as the first post-change diagnostic sample for `ReviewProgressFilterApplicationTests.Model_filter_requires_both_model_id_and_exact_hash`.
3. Accumulate two additional representative clean diagnostic runs after the migration before removing that one quarantine entry.
4. When the three-run criterion is met, remove only the review-progress entry and verify the required shard executes it exactly once.
5. Then migrate the next remaining ad-hoc quarantined API host, preserving one-at-a-time attribution.
6. Use normal subsequent PR timings as the remaining WI-0070 representative samples; do not manufacture redundant reruns solely to satisfy the sample count.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0070-pr-validation-streamlining.md`
- `docs/delivery/work-items/WI-0071-stabilize-quarantined-integration-tests.md`
- `docs/operations/testing-and-ci-strategy.md`
- `AGENTS.md`
- `.github/workflows/build.yml`
- `.github/flaky-integration-tests.txt`
- `.github/scripts/run-integration-shards.ps1`
- `.github/scripts/run-flaky-integration-diagnostics.ps1`
- `.github/scripts/summarize-test-timings.ps1`
- `.github/test-timing-baseline.json`
- `tests/PhotoIdentity.Integration.Tests/PhotoIdentityApiTestFactory.cs`
- `tests/PhotoIdentity.Integration.Tests/ReviewProgressFilterApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
