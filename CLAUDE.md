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
- Before saying "done": paste the command you ran and the last lines of its output.

## Hard rules (also enforced by hooks/deny rules — don't try to work around them)

- Originals (OneDrive/source folders) are read-only. Never write, move, rename or delete them.

- No third-party face recognition services. Face detection/recognition runs locally or in our own
  Azure Container Apps only. Captioning uses our own temporary Azure Container App (ADR-0011/0013).
- Never commit personal data: photos, names, crops, embeddings, catalogue dumps, private paths, reports.
- Before any schema migration or bulk catalogue write: run the backup skill (/db-backup) and confirm it verified.
- Never push to main, never merge PRs, never force-push. Branch → PR → maintainer merges.

## Scope and stopping

- Each task comes from one GitHub issue with acceptance criteria, a budget and a label (spike|build|cleanup).
- Stop and report when: acceptance criteria are met; the budget is used up; or two attempts at the same
  problem failed. Report what you tried, the numbers, and a recommendation. Don't keep tuning.
- "Good enough" thresholds in the issue are the target. Do not optimize beyond them.
- Don't add new safety layers, flags or gates unless the issue asks for it. Prefer deleting code to adding it.
- One PR per issue. Keep diffs small (aim < 400 changed lines excluding generated/moved files).

## Code conventions that aren't obvious from the code

- New code must not copy the "governed/bounded/opt-in" multi-flag CLI pattern from WI-0155 experiments;
  experiments live in experiments/ or tools/ and must not be referenced from src/.
- SQLite is retired (WI-0163). Don't add or extend SQLite code; flag remnants for removal.
- Central package versions live in Directory.Packages.props; don't put versions in .csproj files.

## Where things are

- Architecture: docs/architecture/overview.md · Privacy: docs/architecture/security-and-privacy.md
- Decisions: docs/decisions/ (ADRs; append-only) · Operating the app: docs/operations/local-operator-guide.md
