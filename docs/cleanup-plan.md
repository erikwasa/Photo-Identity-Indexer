# Cleanup plan: Claude Code setup, docs, structure and code

> **What this file is:** a one-time execution plan for Claude Code running locally.
> It is *not* a new tracking system. Once session 2 has turned the issue list below into
> GitHub Issues, the issues are the source of truth. Delete this file in the last Phase 1
> PR (issue C-05), since git history keeps it.
>
> **Committing it:** `main` is protected, so commit this on a branch and open a PR.
> If the existing docs validation (`tools/PhotoIdentity.Docs`) rejects an unregistered file
> under `docs/`, Claude can fix that in session 1 or move the file to the repo root.

---

## Current status (2026-11-22)

| Item | Status |
|---|---|
| Ruleset on `main` (PR required, status checks, no force push, no deletion, empty bypass) | ✅ Done. A direct push was rejected. |
| Required status checks: `build-and-test`, `integration-shard-1`, `integration-shard-2` | ⚠️ Added, not yet verified against the workflow (task 1.6) |
| Claude Code (VS Code + PowerShell 7), GitHub CLI | ✅ Installed |
| `.claude/settings.json` | ⚠️ Added, to be reviewed and completed in session 1 |
| `CLAUDE.md`, hooks, skills, subagents | ❌ Not yet |
| WI-0155 (people in videos) | ⏸ Stopped. The code needed is in `main`. Handover: `docs/operations/video-face-wi0155-handover.md`. Branch `wi-0155` is kept as an archive only: never merge, rebase or delete it. |

---

## How to run this plan

1. **Session 1 (guardrails):** In VS Code, start Claude Code and switch to plan mode (Shift+Tab). Paste:
   > Read docs/cleanup-plan.md and do "Session 1" exactly. Show me the plan before editing. Work on branch `chore/claude-setup` and open a PR. Do not merge.
2. **Session 1b (skills, baseline, repo facts):** Run `/clear`, then:
   > Read docs/cleanup-plan.md and do "Session 1b". Same rules.
3. **Session 2 (issues):** Run `/clear`, then:
   > Read docs/cleanup-plan.md and do "Session 2". Show me the final list before creating anything.
4. **Every session after that:** run `/clear`, then `/implement-issue <n>`, `/cleanup-step <n>` or `/spike <n>`. Then `/clear` again, then `/review-pr <n>`. You merge.

Rules that apply to every session:
- One issue per session and one PR per issue.
- Stop when the acceptance criteria are met, the budget is spent, or two attempts at the same problem have failed.
- Claude never merges and never pushes to `main`.

---

## Session 1: guardrails (one PR: `chore/claude-setup`)

### 1.1 Create `CLAUDE.md` at the repo root

Start from this draft. Fix any command that turns out to be wrong after you check it, and keep the file under about 80 lines.

```markdown
# Photo Identity Indexer — instructions for Claude

Personal, private photo/video archive app (.NET 10, PostgreSQL in Podman/WSL2, Windows).
The maintainer is the only user. Photos are irreplaceable. Privacy > features > polish.

## Verify your work (run these yourself; never ask me to run them)
- Build: `./build.ps1`  (or `dotnet build PhotoIdentity.slnx -c Debug`)
- Fast tests: `./test.ps1 -Scope Fast`
- One test class: `dotnet test tests/<Project> --filter "FullyQualifiedName~ClassName"`
- PostgreSQL tests need a disposable DB (Testcontainers, or PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING
  pointing at a throwaway instance — NEVER the production catalogue).
- Format: `dotnet format PhotoIdentity.slnx`
- Before saying "done": show the command you ran and the last lines of its output.

## Hard rules (also enforced by hooks/deny rules — don't try to work around them)
- Originals (OneDrive/source folders) are read-only. Never write, move, rename or delete them.
- Never open photos, video frames, face crops or embeddings. Work from metrics, logs and aggregate JSON.
- Never query the production catalogue for row-level data. Aggregate counts via scripts only.
- No third-party face recognition services. Face detection/recognition runs locally or in our own
  Azure Container Apps only. Captioning uses our own temporary Azure Container App.
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
- Keep diffs small (aim < 400 changed lines excluding pure moves). Do moves in their own PR.
- If you change behavior described in README or docs/, update that doc in the same PR.

## Code conventions that aren't obvious from the code
- SQLite is retired (WI-0163). Don't add or extend SQLite code; flag remnants for removal.
- Central package versions live in Directory.Packages.props; no versions in .csproj files.
- Experiments live in experiments/ and must not be referenced from src/.

## Where things are
- Architecture: docs/architecture/overview.md · Privacy: docs/architecture/security-and-privacy.md
- Decisions: docs/decisions/ (ADRs, append-only) · Operations: docs/operations/local-operator-guide.md
- Video faces (WI-0155) state: docs/operations/video-face-wi0155-handover.md
```

Check the existing `AGENTS.md`. Claude Code ignores it once `CLAUDE.md` exists. List any rules in it that are still valid but missing from the draft, show me the list, and add only the ones I approve. Leave `AGENTS.md` itself alone; issue C-04 handles it.

### 1.2 Review and complete `.claude/settings.json`

The maintainer has already added a settings file. Merge it with the rules below without removing anything the maintainer added. If the two conflict, ask.

- **Deny:**
  - `gh pr merge`, `git push --force`, `git push -f`, `git push origin main`, `git reset --hard`
  - reads of `*.jpg *.jpeg *.heic *.png *.dng *.mp4 *.mov *.onnx`, `*.dump`, `*.backup` and `.env*`
  - read and edit of the OneDrive root (put the exact path in `.claude/settings.local.json`)
- **Ask:** `git push`, `gh pr create`, `gh issue create`, `dotnet ef`, `podman`, `docker`.
- **Allow:** `dotnet build/test/format/restore`, `./build.ps1`, `./test.ps1`, read-only `git` and `gh` commands, `git add`, `git commit`, `git switch`.
- **Add a `PowerShell(...)` twin for every `Bash(...)` rule.** On Windows, Claude Code may run commands through its PowerShell tool.
- Set `"disableBypassPermissionsMode": "disable"` under `permissions`.
- Add a `.claude/settings.local.json.example` with placeholder paths, and add `.claude/settings.local.json` and `CLAUDE.local.md` to `.gitignore`.

### 1.3 Hooks: PowerShell scripts in `.claude/hooks/`

Check the current hooks docs (https://code.claude.com/docs/en/hooks) for how to wire PowerShell hooks on Windows, and use whatever form works on this machine.

| Hook | Event / matcher | Behavior |
|---|---|---|
| `guard-shell.ps1` | PreToolUse, `Bash\|PowerShell` | Exit 2 with a reason on stderr for: pushing to main or master, force push, `gh pr merge`, any command touching the OneDrive root, and `drop database/table/schema`, `truncate`, `dropdb`, `pg_restore`, `podman rm`, `podman volume rm`. Also blocks a bare `git push` while on main. |
| `guard-paths.ps1` | PreToolUse, `Edit\|Write` | Block edits to `models/**`, `.github/workflows/**` (unless the issue is explicitly about CI) and anything outside the repo root. Return "ask" for migration folders. |
| `format-file.ps1` | PostToolUse, `Edit\|Write` | Run `dotnet format whitespace PhotoIdentity.slnx --include <file>` for edited `.cs` files. |
| `build-on-stop.ps1` | Stop | Run `dotnet build PhotoIdentity.slnx --no-restore -v q`. On failure, exit 2 with the errors so Claude can't finish with a broken build. Never run tests in this hook. |

Starting point for `guard-shell.ps1`:

```powershell
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$cmd = [string]$payload.tool_input.command
$rules = @(
  @{ p = 'git\s+push\b.*\b(main|master)\b';  r = 'Never push to main. Push a feature branch and open a PR.' },
  @{ p = 'git\s+push\b.*\s(--force|-f)\b';   r = 'Force push is not allowed.' },
  @{ p = 'gh\s+pr\s+merge';                   r = 'Only the maintainer merges PRs.' },
  @{ p = '(?i)onedrive';                      r = 'Originals/OneDrive are off-limits to the agent.' },
  @{ p = '(?i)\b(drop\s+(database|table|schema)|truncate\s+table|dropdb|pg_restore|podman\s+(rm|volume\s+rm))\b';
     r = 'Destructive database/container command. Ask the maintainer to run it.' }
)
foreach ($x in $rules) { if ($cmd -match $x.p) { [Console]::Error.WriteLine("BLOCKED: $($x.r)"); exit 2 } }
if ($cmd -match '^\s*git\s+push\s*$') {
  $branch = (git rev-parse --abbrev-ref HEAD).Trim()
  if ($branch -in @('main','master')) { [Console]::Error.WriteLine('BLOCKED: you are on main.'); exit 2 }
}
exit 0
```

### 1.4 Verify (put the results in the PR description)

- [ ] `/context` shows CLAUDE.md is loaded, and `/hooks` and `/permissions` list the expected entries.
- [ ] Each of these is refused: push to main, `gh pr merge`, reading a `.jpg`, a command touching the OneDrive path, `podman volume rm x`.
- [ ] A deliberately broken `.cs` file makes the Stop hook block, and Claude fixes it.
- [ ] Build and fast tests pass.

---

## Session 1b: skills, subagents, baseline, repo facts (one PR: `chore/claude-skills`)

### Skills: `.claude/skills/<name>/SKILL.md`

| Skill | Content |
|---|---|
| `/implement-issue <n>` | 1. `gh issue view n`. 2. Restate the acceptance criteria, budget and type. 3. Plan, and stop for approval if the change is over about 300 lines. 4. Create branch `wi/<n>-slug`. 5. Implement with tests. 6. Run build and fast tests, plus PostgreSQL tests if persistence changed. 7. Self-review the diff. 8. Open a PR with `Closes #n`, evidence (commands and output tails) and a "What I did NOT do" section. 9. Stop. |
| `/cleanup-step <n>` | Same as `/implement-issue`, with these additions: no behavior change; characterization tests must pass before and after; if a behavior change seems necessary, stop and report. |
| `/spike <n>` | Code goes under `experiments/<n>-slug/`. The output is a findings note, posted as a PR or issue comment, with aggregate numbers only. Hard stop at the budget. End with one recommendation: adopt, drop, or a new spike with a new threshold. |
| `/review-pr <n>` | Runs in a fresh context through the `reviewer` subagent. Checks the diff against the issue's acceptance criteria and the CLAUDE.md hard rules. Reports only correctness, privacy and data-loss risks, and skips style. |
| `/db-backup` | `disable-model-invocation: true`. Runs the existing PostgreSQL backup script plus `verify-postgres-backup-restore.ps1` and reports pass or fail. |

### Subagents: `.claude/agents/`

- **`reviewer.md`:** read-only tools (Read, Grep, Glob), plus Bash for `git diff` and `gh pr diff`. Focuses on privacy and data loss, reviewing against the issue.
- **`test-runner.md`:** uses a cheaper model (Haiku). Runs the specified test command and returns only failing test names and the first error lines.

### Baseline and repo facts (post as one comment on the PR)

1. Time taken by `./test.ps1 -Scope Fast` and by the full build, and the warning count per project.
2. The CI workflow jobs and their exact check names. Confirm that `build-and-test`, `integration-shard-1` and `integration-shard-2` match real job names. Also confirm they run on every PR: a required check that never starts blocks merging forever, for example when a test-only classifier or path filter skips the whole workflow. Recommend whether to add or remove any required checks.
3. Facts the plan could not verify:
   - what's in `AGENTS.md`, `BUILD_CONTEXT.md` and `Directory.Build.props`;
   - the list of `src/`, `tests/` and `tools/` projects;
   - `deploy/postgres`;
   - which docs `tools/PhotoIdentity.Docs` validates.

   Correct this plan's issue list wherever these facts contradict it.

Verify by dry-running `/review-pr` on a recent merged PR.

---

## Session 2: create the GitHub Issues

1. **Check the GitHub CLI scope:** `gh auth status`. If the `project` scope is missing, ask the maintainer to run `gh auth refresh -s project`.
2. **Create labels:**
   - type: `cleanup`, `build`, `spike`
   - phase: `phase-1-docs`, `phase-2-structure`, `phase-3-code`, `phase-4-features`
3. **Add `.github/ISSUE_TEMPLATE/workitem.md`** with the template below, in a small PR.
4. **Create the issues below** in the order listed, then fill in the real issue numbers wherever a "Depends on" line refers to another issue.
5. **Create a GitHub Project board** named "Photo Identity Indexer" with the columns Backlog, Ready, In progress, In review and Done. Add all issues, and put C-01, C-02 and V-01 in Ready.
6. **Show the maintainer the final table** (ID → issue number → title) before creating anything, and apply any trims they ask for.

### Workitem template

```markdown
**Type:** build | spike | cleanup    **Budget:** N Claude sessions
**Goal (one sentence):**

**Acceptance criteria (each checkable by a command, a test, or a 5-minute manual check):**
- [ ] ...

**Good-enough threshold (spikes/quality work):**
**Out of scope:**
**Data safety:** touches originals? (must be no) · DB schema change? (→ /db-backup first) · data leaves the machine? (→ only our own Azure apps)
**Verification:** automated: `...` · manual (maintainer, batched): ...
**Stop conditions:** criteria met · budget spent · two failed attempts at the same problem → write findings, stop.
**Depends on:**
```

---

## Issue list

The IDs (C-01 and so on) are only for this file; GitHub assigns the real numbers. Every issue uses the template above, and its "Stop conditions" line is the standard one.

### Phase 1: Documentation (`phase-1-docs`)

**C-01 · Docs inventory: keep / merge / archive / delete** · `cleanup` · Budget: 1
- **Goal:** a table that decides the fate of every file under `docs/`, plus the root `.md` files.
- **Acceptance:**
  - [ ] the table, with a one-line reason per file, is posted as an issue comment;
  - [ ] no files are changed;
  - [ ] `docs/operations/video-face-wi0155-handover.md` is marked keep (it feeds V-01);
  - [ ] the maintainer has approved the table.
- **Out of scope:** any edits.

**C-02 · Move open work items to GitHub Issues** · `cleanup` · Budget: 1
- **Goal:** every active work-item shard becomes a GitHub issue that uses the template.
- **Acceptance:**
  - [ ] one issue per active shard, with its type label, a budget and acceptance criteria. Mark any criteria you inferred with "(inferred, confirm)";
  - [ ] WI-0155 is *not* migrated, because V-01 replaces it;
  - [ ] the issue count equals the number of active shards minus WI-0155;
  - [ ] all issues are on the board.
- **Out of scope:** archived work items, and deleting the shards (C-03 does that).

**C-03 · Retire the delivery registry and the docs gate** · `cleanup` · Budget: 1–2 · Depends on: C-02
- **Goal:** status lives in Issues, not generated YAML.
- **Acceptance:**
  - [ ] `docs/delivery/` is moved to `docs/archive/delivery/`, with a README that says it is historical and not maintained;
  - [ ] `BUILD_CONTEXT.md` is deleted;
  - [ ] the docs-validation job is removed from `build.yml`;
  - [ ] `tools/PhotoIdentity.Docs` and its tests are deleted or moved to the archive, and the solution builds;
  - [ ] references in README and scripts are updated;
  - [ ] CI is green, and the required status checks still pass.
- **Note:** editing `.github/workflows/**` is allowed for this issue.

**C-04 · Replace AGENTS.md and the agent process docs** · `cleanup` · Budget: 1 · Depends on: C-03
- **Acceptance:**
  - [ ] `AGENTS.md` is reduced to a pointer to `CLAUDE.md`, or deleted, whichever the maintainer chooses;
  - [ ] the useful rules from `docs/operations/agent-vertical-slice-delivery.md` are folded into CLAUDE.md, which stays under 100 lines, and that file is deleted;
  - [ ] no doc still instructs agents to avoid GitHub Issues.

**C-05 · Archive historical docs and rewrite `docs/index.md`** · `cleanup` · Budget: 1 · Depends on: C-01, C-04
- **Acceptance:**
  - [ ] the files C-01 marked "archive" are moved to `docs/archive/`, and those marked "delete" are deleted;
  - [ ] `docs/index.md` has about 10 links;
  - [ ] every link in README and in `docs/index.md` resolves (Claude checks with a script);
  - [ ] the README describes the real runtime: Podman/WSL2 PostgreSQL, no SQLite;
  - [ ] `docs/cleanup-plan.md` is deleted.

**C-06 · Rewrite the architecture overview and add an ADR index** · `cleanup` · Budget: 1 · Depends on: C-05
- **Acceptance:**
  - [ ] `docs/architecture/overview.md` is under 2 pages, with a module/dependency diagram (Mermaid) drafted from the actual code;
  - [ ] `docs/decisions/README.md` lists each ADR with a one-line status (accepted/superseded);
  - [ ] the maintainer can explain the system from the overview in 5 minutes.

### Phase 2: Structure (`phase-2-structure`)

**S-01 · Clean the repo root: move scripts into `scripts/`** · `cleanup` · Budget: 1–2
- **Acceptance:**
  - [ ] only `build.ps1`, `test.ps1` and the launcher remain at the root;
  - [ ] the other scripts are moved to `scripts/{db,ops,measure,video,install}/` and all references (CI, docs, launcher, packaging) are updated;
  - [ ] the two backup scripts are merged into one, with the old name kept as a 3-line wrapper or removed;
  - [ ] `verify-wi0091.ps1` is deleted;
  - [ ] `scripts/README.md` has a table of each script, what it does, and whether it is safe to run (read-only / writes DB / touches originals);
  - [ ] CI is green, `build.ps1`, `test.ps1` and the launcher work, and the maintainer has run a backup once with the new path.
- **Data safety:** don't run any DB-writing script; the maintainer runs the backup.

**S-02 · ADR: current dependency graph and target module layout** · `cleanup` · Budget: 1
- **Goal:** decide the target modular-monolith layout before moving any code.
- **Acceptance:**
  - [ ] the ADR shows the current project graph, generated from the `.csproj` references;
  - [ ] it proposes target modules (for example Catalog, Faces, Video, Captioning, Collections, Infrastructure.Postgres, Infrastructure.Inference, SharedKernel) and the dependency rules (hosts → modules → SharedKernel, with infrastructure implementing module interfaces);
  - [ ] it gives an ordered list of extraction steps, least entangled first;
  - [ ] the maintainer has accepted it.
- **Out of scope:** moving code.

**S-03 · Infrastructure folder** · `cleanup` · Budget: 1
- **Acceptance:**
  - [ ] `deploy/postgres` is moved to `infra/postgres` with references updated;
  - [ ] `infra/azure/` holds the caption Container App definition, with parameters and no secrets;
  - [ ] the app starts against the DB;
  - [ ] the maintainer deploys the caption app from the definition once.

**S-04 · First module extraction plus an architecture test** · `cleanup` · Budget: 2 · Depends on: S-02
- **Acceptance:**
  - [ ] the first module from the S-02 list is moved into its own project, with no logic change;
  - [ ] `tests/PhotoIdentity.Architecture.Tests` enforces the dependency rules for that module;
  - [ ] all tests are green;
  - [ ] follow-up issues are created, one per remaining module in the S-02 order.

### Phase 3: Code (`phase-3-code`)

**Q-01 · Hotspot report** · `cleanup` · Budget: 1
- **Acceptance:**
  - [ ] an issue comment ranks: the largest files and classes, the most-changed files, warnings per project, SQLite references, CLI commands and flags with no callers, unused NuGet packages, and anything in `src/` that references experiment or feasibility code;
  - [ ] no edits are made;
  - [ ] a proposed follow-up issue list is included, which the maintainer picks from.

**Q-02 · Testcontainers fixture for PostgreSQL tests** · `build` · Budget: 2
- **Acceptance:**
  - [ ] a shared `Testcontainers.PostgreSql` fixture is added, with the version pinned in `Directory.Packages.props` and the image matching the production major version;
  - [ ] it works with Podman locally and in CI;
  - [ ] at least one env-var-gated test class is converted;
  - [ ] `dotnet test` runs it locally without the environment variable;
  - [ ] follow-up issue(s) are created for the remaining classes.
- **Data safety:** these tests must never be able to reach the production catalogue.

**Q-03 · Analyzer baseline** · `cleanup` · Budget: 1
- **Acceptance:**
  - [ ] `EnforceCodeStyleInBuild=true` and `AnalysisLevel=latest-recommended` are set in `Directory.Build.props`, with warnings still allowed;
  - [ ] warning counts per project are recorded in the PR;
  - [ ] the build and CI stay green;
  - [ ] follow-up issues are created, one per project, to reach zero warnings and then turn on `TreatWarningsAsErrors`.

**Q-04 · Remove SQLite remnants** · `cleanup` · Budget: 1 · Depends on: Q-01
- **Acceptance:**
  - [ ] every SQLite reference from the Q-01 report is removed or explicitly justified;
  - [ ] the SQLite packages are removed from `Directory.Packages.props` if they're unused;
  - [ ] all tests are green;
  - [ ] the diff is mostly deletions.

**Q-05 · Remove unused CLI commands, flags and packages** · `cleanup` · Budget: 1–2 · Depends on: Q-01
- **Acceptance:**
  - [ ] the items from the Q-01 report that the maintainer approved are removed, one category per commit;
  - [ ] characterization tests cover any CLI output that stays;
  - [ ] all tests are green.

### Phase 4: Features (`phase-4-features`)

**V-01 · WI-0155 reassessment: what we have vs. what we need** · `spike` · Budget: 1 · Can run any time after Session 1b, since it's read-only
- **Goal:** decide how to continue with people in videos, with clear boundaries.
- **Inputs:**
  - `docs/operations/video-face-wi0155-handover.md`;
  - the video-face code in `main`;
  - the earlier product goal: at least one useful detection per visible person, with automatic, semi-automatic and manual tiers.
- **Acceptance:**
  - [ ] a findings note (issue comment) covering:
    - (a) what currently works end to end in `main`, with commands;
    - (b) what's partial or dead (flags, gates, unused paths);
    - (c) proposed requirements, with in-scope and out-of-scope lists;
    - (d) proposed measurable thresholds for the maintainer to choose from;
    - (e) a recommendation: continue WI-0155 under a new issue, or close it and open a new item;
  - [ ] a draft body for V-02, plus any cleanup issue for dead video-face code;
  - [ ] no code changes, and no images or frames opened.
- **Out of scope:** tuning, new detectors, and the `wi-0155` branch.

**V-02 · Video face evaluation harness and timeboxed spike** · `spike` · Budget: 3 · Depends on: V-01
- **Before starting:** the maintainer builds the private labeled set: 10–20 videos, a CSV of `video_id, person_alias, present`, stored outside the repo with its path set in `PHOTOID_EVAL_ROOT`.
- **Acceptance:**
  - [ ] one eval script prints aggregate JSON only: person-level recall (top-3), the number of wrong automatic assignments, the automatic/semi-automatic/manual split, and runtime per video-minute;
  - [ ] a baseline run is done;
  - [ ] at most one change per session, with before/after numbers appended to the issue;
  - [ ] the spike stops at the thresholds chosen in V-01 or at the budget;
  - [ ] a final recommendation: build issue / accept manual tagging / move detection to our own Azure Container App.
- **Data safety:** no frames, crops, names or paths in the output, and nothing is sent to third-party services.

---

## Reference: principles behind this plan

- **Checks over documents.** Status comes from CI and from issues closed by `Closes #n`, not from generated YAML.
- **Claude verifies its own work locally.** You run manual checks against your real archive in one batch per week, not after every PR.
- **Thresholds and budgets stop endless iteration.** "Okay" is the target.
- **Spikes produce decisions, builds produce code.** Spike code never becomes production code by accretion.
- **Privacy is enforced by the architecture and the hooks.** Local inference, read-only originals and no third-party services. Each PR doesn't need to add new gates.
- **Anything Claude reads is sent to Anthropic.** That's why Claude gets code, logs and aggregate metrics only, never photos, frames, crops, names or catalogue rows.
