---
name: cleanup-step
description: Do one cleanup issue (type cleanup) with no behavior change, on its own branch, and open a PR. Use when the maintainer runs /cleanup-step <n>.
argument-hint: <issue-number>
---

# Cleanup step for issue #$ARGUMENTS

Follow the /implement-issue steps (.claude/skills/implement-issue/SKILL.md) exactly, with these additions:

- **No behavior change.** Output, persisted data, CLI and HTTP contracts stay the same.
  Doc-only, move-only and delete-only changes are fine when the issue asks for them.
- **Characterization tests before and after.** Before editing, identify (or add) tests that pin the current
  behavior of the code you touch, and run them. Run the same tests after the change. Put both runs
  (command and output tail) in the PR's Evidence section.
- **Moves get their own PR.** If the issue mixes a move with edits, do the move only and say so in
  "What I did NOT do".
- **If a behavior change seems necessary, stop.** Don't make it. Report what you found and why, and
  recommend a separate build issue.
