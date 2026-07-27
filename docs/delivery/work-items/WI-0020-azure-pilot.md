---
id: WI-0020
title: Run Azure VM pilot
milestone: M09
status_source: ../status/work-items.yaml
depends_on: [WI-0018, WI-0032]
affected_modules: [PhotoIdentity.Transfer.Bundles, PhotoIdentity.Worker, docs/azure]
---

# WI-0020: Run Azure VM pilot

## Objective

When Azure access returns, run a small identity-free processing bundle on a temporary VM and compare it with the already-proven local path.

## Acceptance criteria

- [ ] Azure access is confirmed before any resource is created.
- [ ] The VM receives only explicit job bundles and pinned model files.
- [ ] No OneDrive credential, canonical database, person record or human label enters Azure.
- [ ] Result hashes and model provenance match the job contract.
- [ ] Local and Azure outputs agree within documented numerical tolerance.
- [ ] Actual runtime and cost are recorded and the VM is deallocated.

## Scheduling policy

This item is deliberately blocked by the local workflow and documentation validation. Lack of Azure access must not delay WI-0027 through WI-0032 or WI-0025.
