# Build context

## Current milestone

**M00 — Repository and architecture**

## Current work item

**WI-0001 — Establish living documentation**

Status: `in_review`

## Branch and pull request

- Branch: `agent/living-documentation`
- Pull request: [#1 — Introduce living project documentation](https://github.com/erikwasa/Photo-Identity-Indexer/pull/1)

## Objective

Replace the single `docs.md` plan with focused design documents, milestone and work-item files, canonical YAML status registries, ADRs and AI-agent instructions.

## Relevant files

- `AGENTS.md`
- `docs/index.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`
- `docs/delivery/work-items/WI-0001-living-documentation.md`

## Commands that work

No application build commands exist yet.

## Acceptance test

- The documentation index links to all major areas.
- Every milestone from M00 through M14 has a document and registry entry.
- Initial work items have stable IDs and canonical statuses.
- Architectural constraints from the original plan remain represented.
- `docs.md` directs readers to the new structure.

## Known issues

- Generated status tooling does not exist yet; status pages are manually maintained until WI-0004.
- Human review is required before WI-0001 can be marked completed.

## Next action

Review and merge pull request #1, mark WI-0001 completed with evidence, then start WI-0002.
