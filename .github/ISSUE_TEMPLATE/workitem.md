---
name: Work item
about: One unit of work (build, spike or cleanup) with acceptance criteria and a budget
title: ""
labels: []
---

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
