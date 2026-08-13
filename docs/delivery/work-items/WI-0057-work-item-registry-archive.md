---
id: WI-0057
title: Split active and archived work-item registries
milestone: M00
status_source: ../status/work-items.yaml
depends_on: [WI-0004]
affected_modules: [tools/PhotoIdentity.Docs, PhotoIdentity.Docs.Tests, delivery-status]
---

# WI-0057: Split active and archived work-item registries

## Objective

Keep `docs/delivery/status/work-items.yaml` small for routine agent updates while preserving completed/cancelled history for dependency, milestone and audit use.

## Scope

- Preserve the pre-migration registry unchanged under `docs/delivery/status/archive/`.
- Keep `work-items.yaml` for current work.
- Make `PhotoIdentity.Docs` combine current work with terminal archive history.
- Keep archive history read-only during normal status updates.
- Update repository guidance for the split registry layout.

## Acceptance criteria

- [ ] The current registry contains only non-terminal items at migration time.
- [ ] The pre-migration registry is retained unchanged in the archive directory.
- [ ] Current work can resolve completed dependencies from archive history.
- [ ] Stale non-terminal rows in the legacy snapshot do not override current entries.
- [ ] Normal status persistence keeps archive history unchanged.
- [ ] Validation and generated milestone status use the combined view.
- [ ] Tests cover archived dependency resolution and read-only persistence.
- [ ] Agent/tooling guidance describes the split.

## Follow-up boundary

Additional archive batches can be introduced later if the current registry grows materially again; automatic rotation is not required for this migration.

## Verification

Use the normal repository build, test and documentation-validation gates before review.
