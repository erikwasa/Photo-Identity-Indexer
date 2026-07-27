---
id: WI-0032
title: Validate documentation from a clean setup
milestone: M15
status_source: ../status/work-items.yaml
depends_on: [WI-0031]
affected_modules: [docs, verify-local.ps1, verify-review.ps1, tools/PhotoIdentity.Docs]
---

# WI-0032: Validate documentation from a clean setup

## Objective

Prove that the rewritten documentation is complete and comprehensible by following it from a clean Windows setup rather than relying on project memory.

## Acceptance criteria

- [ ] A clean checkout can install models, build, test and run synthetic verification using only the documented steps.
- [ ] The documented local catalogue and review flow works on Windows and Pixel over a trusted network.
- [ ] The 500-image pilot and multi-model comparison procedures identify every required input and expected output.
- [ ] Every command is executed or covered by an automated documentation test where practical.
- [ ] Broken links, stale generated status, unexplained terms and hidden prerequisites are rejected by validation.
- [ ] A second reading pass records confusing sections and resolves them before completion.
- [ ] Azure instructions remain clearly optional and deferred until access is available.
