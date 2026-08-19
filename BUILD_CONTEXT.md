# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** has three independent successful sub-six-minute PR samples after Slice 3: PR #180 at about 3m37s, PR #182 at about 4m15s, and PR #186 workflow #1157 at about 3m04s overall. Later WI-0071 runs continue to show material hosted-runner/setup/test-duration variance, so timing baselines must not be rebalanced from a single slow run.

**WI-0071 — Stabilize quarantined API integration tests** is in its final restoration slice. PR #188 merged the structural generic-host isolation change. PR #190 restored person-visibility and workflow #1177 proved 300 required tests exactly once. PR #192 then restored collection-query and merged as `ffe5a7db4de1fcd22e75d933f6fca93fe35d171a`.

Workflow #1188 (`32293845086`) is the representative PR #192 restoration run: the diagnostic lane ran exactly the one remaining quarantined suggestion-gallery test once with no retries; shard 1 passed **157/157** and shard 2 passed **144/144**, with planned=results=unique and `quarantined-results=0` in both shards. Required coverage therefore increased from 300 to **301**, proving collection-query returned exactly once. Build/fast tests, living/generated documentation, PR `PublishedMinimum` smoke, and mixed-media verification also passed; launcher/package verification were correctly skipped.

The same #1188 run advances suggestion-gallery to **3/3** clean post-change representative samples (`#1166`, `#1177`, `#1188`), so `ReviewSuggestionGalleryApplicationTests.Gallery_requires_exact_model_revision_and_rejects_unknown_sort_or_confidence_group` is restoration-eligible.

The current branch removes that final active quarantine entry. `.github/flaky-integration-tests.txt` remains present as a comment-only file because `run-flaky-integration-diagnostics.ps1` reads it unconditionally and exits successfully when no active entries are configured. A clean representative run should report zero quarantined diagnostics and increase required integration coverage from 301 to **302**, proving suggestion-gallery executes exactly once in the blocking shards.

M19 feature work remains separate and must be preserved when branches are synchronized with `main`.

## Next concrete step

1. Validate the final suggestion-gallery restoration with zero configured diagnostics and exact required-shard coverage totaling **302** tests.
2. If clean, record the representative workflow evidence in the PR without changing the validated head, then merge the final quarantine-removal PR.
3. After merge, reconcile WI-0070/WI-0071 formal lifecycle status and final documentation in a dedicated closeout change; the quarantine list should remain empty unless a future independently justified quarantine is introduced.
4. Do not add automatic retries, weaken assertions, or rebalance `.github/test-timing-baseline.json` from a single hosted-runner outlier.

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
- `tests/PhotoIdentity.Integration.Tests/IdentityMatchRegenerationApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/ReviewSuggestionGalleryApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
