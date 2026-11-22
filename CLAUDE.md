# Photo Identity Indexer — instructions for Claude

Personal, private photo/video archive app (.NET 10, PostgreSQL in Podman/WSL2, Windows).
The maintainer is the only user. Photos are irreplaceable. Privacy > features > polish.

## Verify your work (run these yourself; never ask me to run them)

- Build: `./build.ps1` (or `dotnet build PhotoIdentity.slnx -c Debug`)
- Fast tests: `./test.ps1 -Scope Fast`
- One test class: `dotnet test tests/PhotoIdentity.Core.Tests --filter "FullyQualifiedName~ClassName"`
- PostgreSQL tests need a disposable DB (Testcontainers, or PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING
  pointing at a throwaway instance — NEVER the production catalogue).
- Format: `dotnet format PhotoIdentity.slnx`
- Before saying "done": show the command you ran and the last lines of its output.

## Hard rules (also enforced by hooks/deny rules — don't try to work around them)

- Originals (OneDrive/source folders) are read-only. Never write, move, rename or delete them.
- Never open photos, video frames, face crops or embeddings. Work from metrics, logs and aggregate JSON.
- Never query the production catalogue for row-level data. Aggregate counts via scripts only.
- No third-party face recognition services. Face detection/recognition runs locally or in our own
  Azure Container Apps only. Captioning uses our own temporary Azure Container App (ADR-0011/0013).
- Never commit personal data: photos, names, crops, embeddings, catalogue dumps, private paths, reports.
- Before any schema migration or bulk catalogue write: stop and ask me to run /db-backup.
- Never push to main, never merge PRs, never force-push. Branch → PR → maintainer merges.
- Branch `wi-0155` is an archive. Never merge, rebase, cherry-pick from, or delete it.

## Scope and stopping

- Each task comes from one GitHub issue with acceptance criteria, a budget and a type label
  (spike | build | cleanup). Read it with `gh issue view <n>`.
- Stop and report when: acceptance criteria are met; the budget is used up; or two attempts at the same
  problem failed. Report what you tried, the numbers, and a recommendation. Don't keep tuning.
- "Good enough" thresholds in the issue are the target. Do not optimize beyond them.
- Don't add new safety layers, flags, gates or opt-in switches unless the issue asks for it.
  Prefer deleting code to adding it.
- One PR per issue. Keep diffs small (aim < 400 changed lines excluding pure moves). Do moves in their own PR.
- If you change behavior described in README or docs/, update that doc in the same PR.

## Delivering a feature

- Aim for one coherent, user-visible outcome per issue. A probe, metadata query, synthetic run or SHA check is
  evidence toward a feature, not the feature. If production processing isn't enabled yet, say
  "evaluation checkpoint accepted; feature not delivered".
- Don't split work into prep-only PRs (one more helper CLI, a PR just to record that the last one merged).
  If an extra PR or checkpoint is needed, say why.
- Put reproducible proof in CI, including negative cases (stale/excluded revision, online-only source, digest
  mismatch, timeouts, partial output, cleanup). Red CI is yours to fix on the branch. Never ask me to test an
  unmerged branch.
- Manual checks only for real private media or visual judgment: after merge to main, one short copyable procedure
  that says which commands read originals, change the DB, hydrate OneDrive or publish derivatives.
- Never auto-hydrate OneDrive or process online-only/unknown-availability originals. A synthetic or metadata match
  never authorizes reading real originals. Never claim production-ready on synthetic evidence alone.
- Choose and report expansions by additional usable assets at a named stage. Keep candidate counts (upper bound)
  separate from verified working outputs.

## Code conventions that aren't obvious from the code

- Keep the solution a modular monolith until an accepted ADR says otherwise.
- SQLite is retired (WI-0163). Don't add or extend SQLite code; flag remnants for removal.
- Central package versions live in Directory.Packages.props; no versions in .csproj files.
- Experiments live in experiments/ (or tools/) and must not be referenced from src/. New code must not
  copy the "governed/bounded/opt-in" multi-flag CLI pattern from the WI-0155 experiments.
- Don't normalize flaky tests with unconditional retries; any quarantine must stay visible and tracked.

## Where things are

- Architecture: docs/architecture/overview.md · Privacy: docs/architecture/security-and-privacy.md
- Decisions: docs/decisions/ (ADRs, append-only) · Operations: docs/operations/local-operator-guide.md
- Video faces (WI-0155) state: docs/operations/video-face-wi0155-handover.md
