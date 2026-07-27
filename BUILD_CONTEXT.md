# Build context

## Current milestone

**M06 — Local evaluation and acceptance**

Status: `ready`

## Next ready work

- **WI-0027 — Complete the local review workflow**
- **WI-0028 — Export reviewed catalogues to model-lab**

These items are intentionally parallel. WI-0029, the 500-image local acceptance pilot, starts only after both are complete.

## Recently completed

**WI-0017 — Add evaluation harness** merged through pull request #34 at `d0093e1a817dd81c905cc3edf908f35e8fe4b65f` on 2026-07-27. The deterministic evaluation command separates gallery, validation and held-out test data, records exact model provenance and reports accuracy, confusion and throughput.

## Planning branch

- Branch: `agent/local-first-delivery-plan`
- Pull request: pending creation

## Delivery objective

Prove as much of the product as possible without Azure:

1. complete sustained browser review on Windows and Pixel;
2. export evaluation data from the reviewed catalogue;
3. run a representative 450–550 image baseline pilot;
4. add a second model and repeat the same corpus;
5. exercise practical collection queries; and
6. rewrite and independently validate the operator and architecture documentation.

Azure execution resumes after this phase and only when access is available.

## Current implementation gaps addressed by the next items

- Ranked suggestions exist in persistence but are not yet an operator workflow in the browser.
- Person rename/merge, safe bulk review and revision-aware progress are not yet complete.
- The evaluation harness consumes a prepared manifest but does not yet export one from the catalogue.
- Multi-model outputs are versioned in the architecture, but a second adapter and revision-aware comparison workflow are not yet implemented.

## Relevant planning files

- `docs/delivery/local-first-plan.md`
- `docs/operations/local-evaluation.md`
- `docs/delivery/milestones/M06-evaluation.md`
- `docs/delivery/milestones/M08-second-model.md`
- `docs/delivery/milestones/M15-documentation.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`

## Validation for this planning change

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```
