# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**WI-0163 Add safe bulk capture-date and Place enrichment is in progress under M31 Bulk archive metadata enrichment. WI-0148 is in review under M29 after moving active test/tool verification from SQLite to PostgreSQL.**

The maintainer measured 17,892 current photos: 234 have no effective capture date, 12,603 have no named Place or valid non-zero GPS, and 12,371 of the location-less photos already have an effective date. Directory `1970` is a miscellaneous catch-all and must never be interpreted as a real capture year merely from its path.

WI-0163 remains on `agent/wi-0163-bulk-metadata-enrichment`. Its first CLI slice adds `metadata enrich`, which is dry-run by default, reads explicit JSON rules, proposes missing dates from conservative filename/path patterns, and proposes Places only when an effective date range is fully contained by an operator-supplied rule. `--apply` uses the existing PostgreSQL capture-date and Place repositories; existing effective dates, named Places and valid non-zero GPS are protected by default.

M23 Source-copy lifecycle and privacy exclusion is completed with all five work items and the maintainer real-catalogue acceptance recorded. M28 and M26 remain completed. WI-0147 is completed with maintainer verification of PostgreSQL-only packaged startup/restart and the live PostgreSQL verifier. WI-0148 ports active tests and review verification to PostgreSQL, removes obsolete provider-specific coverage and records the exact remaining implementation/CLI compatibility surface for WI-0149. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Continue WI-0163 from its canonical work-item/status shard. For M29, review WI-0148 verification and then start WI-0149 to remove the retained SQLite implementation and compatibility names.

## Relevant files

- docs/delivery/milestones/M31-bulk-metadata-enrichment.md
- docs/delivery/work-items/WI-0163-bulk-metadata-enrichment.md
- docs/delivery/status/work-items/active/WI-0163.yaml
- docs/delivery/work-items/WI-0148-retire-sqlite-test-tool-dependencies.md
- docs/delivery/status/work-items/active/WI-0148.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
