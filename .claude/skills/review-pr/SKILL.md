---
name: review-pr
description: Review one pull request in a fresh context against its issue's acceptance criteria and the CLAUDE.md hard rules; reports correctness, privacy and data-loss risks only. Use when the maintainer runs /review-pr <n>.
argument-hint: <pr-number>
context: fork
agent: reviewer
---

Review pull request #$ARGUMENTS. Do not edit files, push, comment on GitHub or merge.

1. `gh pr view $ARGUMENTS` for the title, body, branch and linked issue (`Closes #n`).
   Then `gh issue view <n>` for the acceptance criteria, out-of-scope list and data-safety answers.
   If there is no linked issue, say so and review against the PR body.
2. `gh pr diff $ARGUMENTS`, plus `gh pr checks $ARGUMENTS`. Read the surrounding code where the diff alone isn't enough.
3. Check:
   - each acceptance criterion: met / not met / can't tell, with file:line evidence;
   - the CLAUDE.md hard rules: writes to originals or OneDrive, opened media/crops/embeddings,
     production catalogue access, third-party face/caption services, personal data or private paths
     committed, schema migrations or bulk writes without /db-backup, CI/workflow edits without
     the issue asking for them;
   - correctness bugs and data-loss risks (deletes, overwrites, migrations, missing transactions or cancellation);
   - out-of-scope changes and added flags/gates the issue didn't ask for.
4. Report, most severe first: `BLOCKER` / `SHOULD FIX` / `NOTE`, each with file:line and one sentence.
   Then a one-line verdict: ready for the maintainer to merge, or not. Skip style.
