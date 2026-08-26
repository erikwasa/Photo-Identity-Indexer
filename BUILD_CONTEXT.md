# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

The consolidated M19/M20 maintainer verification completed on **2026-08-26**. The authoritative result is `docs/delivery/milestones/M20-maintainer-verification-2026-08-26.md`.

The maintainer reported **PASS** for every item in the planned review checklist:

- WI-0072 archive metadata integration on real media;
- WI-0064/WI-0065 automatic GeoNames pickup, restart/resume, manual precedence and Sweden-local/else-English language policy;
- WI-0073 Face Review/Smart Collection/Maintain People/archive-state corrective polish;
- WI-0075 corrected GeoNames timing overrides/effective diagnostics;
- WI-0077 compact Photo Details metadata/location presentation;
- previously accepted WI-0074 and WI-0078 remained regression-free.

M19 is therefore complete. WI-0073, WI-0074, WI-0075, WI-0077 and WI-0078 are also accepted for completion.

WI-0076 remains separate archive-throughput work in PR #200 and is not part of the final acceptance result above.

Immediately after the successful checklist, the maintainer reported three **new** issues. They are grouped into **M21 — Reliability and recognition quality** and must be investigated before selecting fixes:

1. **Critical — WI-0079:** `Sync included folders` is taking increasingly long and recent requests fail with `net_http_request_timedout, 100`.
2. **High — WI-0080:** some face review images contain two visible faces, making the detected/review target ambiguous.
3. **Medium — WI-0081:** identity suggestion accuracy appears to be degrading as the real catalogue/reference corpus grows.

Do not treat these findings as failures of the already-passed M19/M20 checklist. Do not silently fold them into WI-0076.

## Next concrete step

Start the next investigation with **WI-0079**.

1. Read WI-0079 and trace the exact `Sync included folders` request path.
2. Reproduce/characterize the timeout on representative catalogue size.
3. Add or use phase timing/count diagnostics to determine the dominant scaling cost and request-cancellation semantics.
4. Compare correction strategies and return the evidence/options to the maintainer before implementing a fix.
5. After WI-0079 direction is agreed, investigate WI-0080 and then WI-0081 in priority order unless new evidence changes severity.
6. Keep WI-0076/PR #200 isolated from these investigations unless measured evidence establishes a genuine shared cause.

## Relevant files

- `docs/delivery/milestones/M20-maintainer-verification-2026-08-26.md`
- `docs/delivery/milestones/M21-reliability-recognition-quality.md`
- `docs/delivery/work-items/WI-0079-included-folder-sync-timeout.md`
- `docs/delivery/work-items/WI-0080-detected-face-clarity.md`
- `docs/delivery/work-items/WI-0081-suggestion-accuracy-degradation.md`
- `docs/delivery/work-items/WI-0076-archive-throughput.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`
- `AGENTS.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
