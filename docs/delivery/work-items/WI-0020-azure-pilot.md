---
id: WI-0020
title: Run Azure VM pilot
milestone: M09
status_source: ../status/work-items.yaml
depends_on: [WI-0018]
affected_modules: [infra/azure, PhotoIdentity.Worker]
---

# WI-0020: Run Azure VM pilot

## Objective

Run a small portable bundle on a temporary Azure VM using interactive provisioning and SSH/SCP, then import and compare results locally.

## Acceptance criteria

- [ ] No app registration, service principal or managed identity is created.
- [ ] No OneDrive credential or canonical database enters Azure.
- [ ] Local and Azure outputs match within tolerance.
- [ ] Actual cost is recorded and the VM is deallocated.
