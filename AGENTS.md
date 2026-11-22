# AI agent instructions

## Read before changing the repository

1. Read the GitHub issue for the task (`gh issue view <n>`): acceptance criteria, budget, type label and out-of-scope list.
2. Read [the agent vertical-slice delivery approach](docs/operations/agent-vertical-slice-delivery.md) before planning feature work: prefer complete outcomes, CI-first checks and archive-value prioritization while preserving every safety gate.
3. Read only the linked ADRs and module documents needed for that issue.

## Architecture constraints

- Use C# and .NET by default. Use Python only for isolated model experiments, conversion or analysis where it is materially better.
- Keep the solution a modular monolith until an accepted ADR says otherwise.
- Core/domain code must not expose EF Core, OpenCV, ONNX Runtime, Azure SDK or Microsoft Graph types.
- Personal OneDrive is accessed through the Windows sync client, not Microsoft Graph.
- Local processing is the normal default; ADR-0013 allows purpose-scoped opt-in inference on operator-managed Azure resources, not unvetted opaque AI services. PostgreSQL, OneDrive access, source/privacy decisions and canonical authority remain local.
- PostgreSQL is the sole writable production catalogue. The SQLite implementation and executable compatibility paths are retired; preserved SQLite material is historical migration evidence only unless a future ADR changes that authority boundary.
- Canonical people and identity assignments are model-independent and auditable. ADR-0006 permits opt-in canonical automatic assignments with exact-model/policy provenance.
- Original photos are read-only and must not be modified.
- The permanent archive uses one stable source identity with bounded local materialization; see ADR-0007.
- M30's opt-in video ingestion, local processing, playable proxies, selected frame evidence and Video Search have been completed under documented bounded scopes (WI-0150/0151/0188/0189/0190/0191). **WI-0152 browser playback is merged, maintainer-accepted and archived (PR #720)**; WI-0210's merged PR #722 reports 58 current `photoidentity_main` videos with 12 isolated Rec.709 yuvj420p proxy metadata candidates, but ALL 58 source availabilities unknown; PR #724 merged and the guarded synthetic full-range proxy roundtrip PASSED on the maintainer's Windows FFmpeg; checkpoint 5 live availability verified 12/12 Rec.709 yuvj420p pixel-only candidates as local size matches (not SHA or colour proof). Checkpoint 6 merged PR #727 operator shortlist is accepted (12/12, readOnly, no source bytes opened or hydration); next add an optional exact Rec.709 yuvj420p pixel-only metadata guard to the existing explicit no-recall SHA source-proof command, preserving separate --read-local-bytes authorization and production isolation.Next develop explicit opt-in private UUID-only candidate selection without content opens, hydration or job writes. Production gate/HDR remain unchanged until approved real-source checks; local review starts on merged main; WI-0207 independently owns online-only video hydration/release. Keep source/revision, original-file and existing photo contracts. Video face recognition is desired but starts with a bounded experimental WI-0155 prototype; mixed collections/slideshows are postponed and Live Photo pairing administratively retired. Consult M30's current section, not historical activation instructions.

**WI-0210 delivery handoff (2026-11-04):** PR #728's
exact-revision local original SHA passed on the maintained
Windows host with no hydration or worker side effects.
Under the [agent vertical-slice guide](docs/operations/agent-vertical-slice-delivery.md),
implement the **whole guarded real-source conversion evaluation
in one PR** (input probe + bounded full→limited conversion +
output validation + post-trial SHA + private review), then
only after real visual acceptance implement one cohesive
production playable-proxy integration. No new small
prep-only PRs, production admission or unapproved original
read. Local verification starts after merge on `main`.

## Scope discipline

- Follow [the agent vertical-slice delivery guide](docs/operations/agent-vertical-slice-delivery.md): aim for a coherent user-visible outcome (often one or two reviewable PRs), put reproducible proof in CI, and prioritize archive changes by additional *usable* assets at an explicitly named stage. Record justified exceptions rather than multiplying preparatory PRs or local acceptance sessions.
- Local operator acceptance begins only after merge into `main`. Any original read, OneDrive hydration or production worker admission still requires its established explicit authorization and guards.
- Work on one issue at a time: one issue, one branch, one PR.
- Do not broaden an issue silently. Create a follow-up issue for unrelated work.
- A contract change must be explicit and documented.
- Avoid unrelated refactoring.
- Keep model preprocessing beside the relevant adapter.
- Do not revive the retired M09–M11 Azure VM/checkpoint roadmap. ADR-0013 authorizes optional operator-controlled inference; any new remote implementation beyond the existing caption bridge needs scoped repository work, documented safety controls and verification.

## Privacy

Never commit personal photos, face crops, embeddings, biometric datasets, model binaries, credentials, tokens, private paths or large generated logs.

## Work items

Work items are GitHub Issues with acceptance criteria, a budget and a type label (`spike`, `build` or `cleanup`). Status lives in the issue and its PR, not in repository files. The old `docs/delivery/` registry (WI-XXXX documents, milestones and YAML status shards) is historical and not maintained; read it only when an issue links to a specific record.

## Testing and pull-request validation

- Put behavior at the lowest practical test layer. Use full HTTP-host integration tests for cross-layer wiring and contracts that cannot be established more cheaply.
- Generic API integration tests should reuse the shared test host and keep unrelated production background workers disabled. Worker-specific behavior should opt in explicitly or exercise the worker directly.
- The host-heavy integration assembly remains sequential in-process. Do not re-enable broad xUnit parallelism without evidence that host isolation has changed enough to make it safe.
- Do not normalize flaky tests with unconditional retries. Temporary quarantine must stay visible, have a tracked stabilization follow-up and an explicit condition for returning to the required gate.
- When adding host-heavy tests or required CI checks, state why the added signal justifies the runtime cost and include timing evidence when the cost is material.
- PR descriptions should state the test layer added or changed, whether the required CI gate changed, and any material timing or coverage tradeoff.
- Keep detailed rationale in `docs/operations/testing-and-ci-strategy.md`; keep this file concise and action-oriented.

## Definition of done

- Relevant code builds.
- Relevant tests pass.
- Cancellation and errors are handled where applicable.
- Logging contains no sensitive data.
- Database changes include migrations.
- Idempotency has been considered.
- The affected documentation is updated.
- Evidence (commands run and their output tails) is recorded in the PR description.
