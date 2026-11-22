---
name: implement-issue
description: Implement one GitHub issue (type build) end to end on its own branch and open a PR. Use when the maintainer runs /implement-issue <n>.
argument-hint: <issue-number>
---

# Implement issue #$ARGUMENTS

Follow CLAUDE.md. One issue, one branch, one PR. Never merge, never push to main.

1. Read the issue: `gh issue view $ARGUMENTS`. If it has no acceptance criteria, budget or type label, stop and say so.
2. Restate, in a few lines: the goal, each acceptance criterion, the budget (Claude sessions), the type,
   the out-of-scope list and the data-safety answers.
3. Plan the change. List the files you expect to touch and estimate the changed lines.
   If it is over about 300 lines, or needs a schema migration or bulk catalogue write,
   stop and wait for approval (for migrations, ask the maintainer to run /db-backup first).
4. Create the branch: `git switch -c wi/$ARGUMENTS-<short-slug>` from an up-to-date `main`.
5. Implement with tests at the lowest practical layer. Don't add flags, gates or opt-in switches the issue
   didn't ask for. Prefer deleting code. If behavior described in README or docs/ changes, update that doc.
6. Verify, and keep the last lines of each output:
   - `./build.ps1`
   - `./test.ps1 -Scope Fast`
   - If persistence changed: the relevant PostgreSQL tests against a disposable DB only
     (Testcontainers or PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING to a throwaway instance).
7. Self-review `git diff main...HEAD`: each acceptance criterion met? Any personal data, private paths,
   writes to originals, or production catalogue access? Anything beyond scope? Fix or remove it.
8. Commit, push (`git push -u origin <branch>`) and open the PR with `gh pr create`. The body has:
   - `Closes #$ARGUMENTS`
   - Summary (2-5 bullets) and the acceptance criteria as a checklist
   - Evidence: each command run and the tail of its output
   - What I did NOT do: skipped criteria, deferred work, manual checks left for the maintainer
9. Stop. Report the PR URL and anything left for the maintainer. Do not start another issue.

Stop early and report (what you tried, the numbers, a recommendation) if the budget is spent or two
attempts at the same problem have failed.
