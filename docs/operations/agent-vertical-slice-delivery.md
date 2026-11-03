# Agent delivery approach: vertical slices, CI-first verification and archive value

**Audience:** AI coding agents and maintainers planning or implementing Photo
Identity work items. **Type:** standing engineering-delivery guideline, not
an architecture decision record. Apply alongside `AGENTS.md`, the current
work-item acceptance criteria, and
[testing and CI strategy](testing-and-ci-strategy.md). Existing ADRs and
privacy/source policies take precedence if a delivery shortcut conflicts
with them.

## Purpose

Favor observable end-user progress over a sequence of individually merged
preparatory commands. WI-0210's early video-compatibility work demonstrated
that several safe but tiny PRs, repeated CI cycles and repeated operator
checkpoints can consume substantial effort before any newly supported
archive video becomes playable. Keep the useful safeguards; consolidate
how they are implemented and verified.

**Default objective:** For one well-defined work item, reach one useful
end-to-end behavior with as few handoffs and operator interventions as
practical. A passing metadata command, probe, synthetic converter or SHA
check is *evidence toward* a feature, not the completed user-visible feature.

## 1. Develop complete vertical slices

### Plan from the user outcome backwards

Before writing implementation code, put these items in the work item:

1. **User-visible result and stage:** say exactly what becomes possible,
   such as “previously blocked Rec.709 `yuvj420p` videos gain playable
   proxies,” not merely “add a format parser.” State whether the scope is
   inspection, playable proxy, poster, frame evidence, search or UI playback.
2. **Current archive evidence:** quantify the candidate class, current
   blocking gates, confirmed local sources and known uncertainty. Identify
   representative private real-media acceptance needs **without** putting
   filenames or IDs in repository documents.
3. **End-to-end path:** list the minimal affected gates and components
   from source admission through output and user consumption. Reuse
   existing providers, safe worker budgets, no-recall handles, FFprobe/
   FFmpeg profiles, PostgreSQL contracts, publishers and playback paths.
4. **Acceptance:** define automated negative/positive tests, an
   operator-only real-media check if needed, and an objective before/after
   count of assets unlocked at the promised stage.
5. **Decision boundary:** state what needs operator approval. A
   synthetic test or metadata match never silently authorizes
   processing real private originals, hydration or production admission.

### Make cohesive changes, not PRs per guard

Aim for **one or two substantial, reviewable PRs per scoped expansion**,
where that is consistent with safety and reviewability:

- **Evaluation slice (when real-media validation is necessary):**
  exact-revision authorization, input inspection, bounded experimental
  transform, independently checked output, cleanup and regression tests
  in one coherent workflow. Where the input is private, CI covers
  synthetic/isolated contracts and the operator runs **one consolidated
  acceptance session after merge to `main`**.
- **Product slice:** only after the evaluated class is accepted,
  integrate the versioned processing gate, derivative publication,
  relevant UI/playback and tests, then verify one end-to-end user-visible
  result. Avoid silently changing poster, frames, HDR or unrelated
  stages when the promised stage is playable proxy only.

These are *logical review boundaries, not mandatory PR counts*.
Combining them into one safely gated PR is fine; splitting further is
fine when a material security boundary, risky migration, independently
reviewed decision or CI/runtime limitation demands it. Record **why**
an extra PR or local checkpoint is needed and what new evidence it
will unlock. Do not split merely because one more helper CLI is easy
to introduce.

Prefer extending the existing guarded workflow over building a new
metadata-only CLI for each substep. Never bypass source-control checks,
exclusions, race protection, resource budgets or privacy to reduce PRs.

**Stop rule:** If the real sample fails input colour/geometry proof,
output appearance or integrity, stop that class's production rollout,
record the limitation and revise the evaluation. Never claim a
production-ready compatibility expansion on synthetic evidence alone.

## 2. Move reproducible verification into CI

**Every PR should arrive with its automatable evidence**, not require
the maintainer to replay CI on the Windows production machine.

| CI owns (safe, reproducible) | Maintainer's Windows host owns (cannot be simulated reliably) |
| --- | --- |
| Compilation, unit and policy gates | Access to intentionally selected private archive originals |
| Disposable PostgreSQL lifecycle/queries, source and exclusion fencing | Current OneDrive Files On-Demand state without accidental recall |
| Synthetic/fixture FFprobe and FFmpeg contracts where CI tools support them | SHA verification of an explicitly approved already-local original |
| Bounded encoding, output validation, error/timeout/cleanup regressions | Actual camera colour/contrast and rotation assessment |
| Publisher/HTTP/API/UI tests with synthetic test data | Final desktop/phone playback with the user's real archive |
| Docs validation, generation and status-consistency checks | Operator judgment where a production capability must be enabled |

- Use the **cheapest sufficient test layer** and existing CI setup;
  only add expensive host tests for real cross-component contracts.
  Do not put private archive data, personal media or live production
  credentials in CI. Do not simulate real-camera colour acceptance
  by asserting only synthetic output signalling.
- Cover negative cases **inside the cohesive PR**: incorrect format,
  changed/stale/excluded revision, online-only source, digest mismatch,
  unsafe paths, unexpected streams, colour or rotation mismatch,
  bounds/timeouts, partial outputs and cleanup. Do not wait for the
  operator's production command to uncover ordinary compilation,
  Npgsql reader-order or control-flow issues that a disposable
  integration test can catch.
- Treat red CI as agent-owned troubleshooting: inspect the failing
  check/log, fix the PR branch and repeat CI. Do **not** ask the
  operator to test an unmerged branch.
- Keep any manual verification **short, copyable and consolidated**,
  run only **after the PR merges and the operator updates `main`**.
  State precisely which commands read originals, change database
  state, hydrate sources or publish derivatives. For real-source
  access, require the established explicit opt-in separately.
- **Do not request local documentation validation/generation** as
  a routine acceptance step; CI owns those. Do not repeat an already
  accepted synthetic or playback regression without a material change
  affecting it.
- If CI cannot exercise an essential behavior, document the coverage
  gap and the **single operator acceptance test**. Do not mark the
  behavior tested or production-ready based solely on a mock.

The detailed test-layer and CI-budget rules remain in
[testing-and-ci-strategy.md](testing-and-ci-strategy.md).

## 3. Prioritize by actual videos unlocked

For archive-driven video compatibility, prefer **evidence-based,
stage-specific value** over alphabetical formats, estimated ease alone,
or a raw codec count.

### Measure rather than assume

Create a sanitized, read-only catalogue projection before choosing
the next expansion. For each candidate class record:

- **Potential cohort:** current nonexcluded revisions that would
  pass a named stage if *this* gate changed, with other gates held
  constant. Show overlaps/multiple blockers separately. Do not add
  overlapping isolated-gate cohorts together as if all were unlocked.
- **Real availability:** locally confirmed versus online-only/
  unavailable/unknown; distinguish stale catalogue observations from
  current no-recall filesystem attributes. A metadata/size match is
  not a digest or colour proof.
- **What becomes useful:** additional playable proxies, posters,
  frame evidence or search results. State the exact intended stage
  and whether an existing receipt already satisfies it.
- **Effort and uncertainty:** reusable implementation pieces,
  expected engineering/verification complexity and whether colour
  management, rotation, rare codecs, privacy or OneDrive availability
  adds risk.
- **Representative proof:** whether a safe already-local candidate
  exists for an explicitly authorized real verification, and what
  remains unproven.

Rank candidates by expected **additional usable assets at the desired
stage** relative to implementation effort and risk. A small
missing-rotation class might be cheaper than a larger HDR/HLG class
requiring careful tone mapping; neither is automatically preferred
without archive evidence. Defer unsupported or unrepresented cases
rather than treating all video formats as one unlimited work item.

### Report delivered coverage

After each expansion, update the compatibility matrix and work item
with:

- Baseline and post-change **newly stage-eligible** counts, separated
  from the count of **actually processed and playable** assets.
- The tested real-media outcome and any unresolved colour, playback,
  availability or exclusion limits.
- What remains blocked, and the *next highest-value* independently
  scoped expansion, without automatically starting it.

Never present the number of candidate files as a verified number of
working outputs. If there are no confirmed local examples, keep the
cohort as a potential upper bound until evidence changes.

## Agent planning and PR checklist

Before implementation, write a short delivery plan in the existing
work item: **one user outcome, scoped stage, archive evidence, main
gates, automated proof, minimum operator-only acceptance, and
expected unlocked count**. Record dependencies and explicit operator
decisions. Continue to use repository-native WI/ADR/status processes;
this guide does not replace them.

In the PR description, identify the **end-to-end behavior** it moves
toward or completes; what is deliberately deferred; CI tests and
their layers; any missing CI proof; when an original may be read;
the one post-merge local acceptance procedure; and whether the change
has actually unlocked user-visible assets. Avoid separate PRs solely
to record that the previous PR merged or that a metadata query passed.

A scoped feature is **done** only when its promised stage works for
the accepted real examples (where required), guardrails and negative
regressions pass, generated/docs checks pass in CI, operator acceptance
is recorded, and actual delivered coverage is reported. If an
experimental prerequisite passes but production processing is not
enabled, say **“evaluation checkpoint accepted; feature not delivered”**.

## Non-negotiable boundaries

Fewer PRs or local tests must never mean: automatic OneDrive hydration;
processing online-only/unknown-availability originals; unrestricted
FFmpeg invocations; mutating originals; disclosure of filenames,
private paths, revision UUIDs, digests, faces or credentials; direct
writes to the production catalogue from test fixtures; unvetted
third-party processing; or activating wider worker/codec policies
without approved real output. Use the existing local source,
PostgreSQL, digest-pinned tool, concurrency/priority and publisher
controls. Operator-managed Azure processing remains subject to
the relevant ADR and explicit configuration, not an automatic
substitute for local processing.

**Rule of thumb:** keep every safety gate, combine development
and tests around a usable outcome, and spend the maintainer's local
verification effort only where real private data or human visual
judgment makes it indispensable.
