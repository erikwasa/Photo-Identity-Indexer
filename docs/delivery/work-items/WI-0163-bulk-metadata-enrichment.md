---
id: WI-0163
title: Add safe bulk capture-date and Place enrichment
milestone: M31
status_source: ../status/work-items.yaml
depends_on: [WI-0063, WI-0141]
related_adrs: []
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0163: Add safe bulk capture-date and Place enrichment

## Objective

Make archive-scale correction of missing capture dates and locations practical without editing photos one by one and without changing source originals or extracted metadata.

The maintainer measured 17,892 current photos, including 234 with no effective capture date and 12,603 with no named Place or valid non-zero GPS. Of the location-less photos, 12,371 already have an effective capture date, so explicit date-range-to-Place rules can remove substantial manual work while keeping the maintainer in control of the facts being asserted.

## Scope

- Add a PostgreSQL-only CLI command for planning and applying catalogue metadata enrichment.
- Keep dry-run as the default; require an explicit `--apply` flag before any catalogue write.
- Accept operator-owned JSON rules rather than silently learning or guessing locations.
- Infer missing capture dates conservatively from supported filename forms:
  - leading `YYYYMMDD`, including camera-style timestamp filenames,
  - `IMG-YYYYMMDD-*`, `Screenshot_YYYYMMDD-*` and equivalent supported prefixes,
  - plausible 13-digit Unix-millisecond filenames.
- Infer month-precision dates from `YYYY/MM/...` source paths when no more precise filename date is available.
- Exclude configured miscellaneous/catch-all directories from directory-date inference; `1970` is excluded by default and must never be treated as an actual capture year merely because it is the first directory segment.
- Treat conflicting filename and directory dates as ambiguous and do not write either value.
- Accept explicit inclusive date-range-to-Place rules.
- Apply a Place rule only when the photo's entire effective date range is contained inside the rule. A month- or year-precision value that merely overlaps a trip is ambiguous rather than a match.
- Do not replace an existing effective capture date, named Place, or valid non-zero GPS location by default.
- Recheck current state immediately before apply so changes made after a dry-run are not overwritten.
- Persist accepted changes through the existing append-only capture-date and Place repositories so provenance, hierarchy creation and idempotence remain consistent with UI edits.
- Optionally write a local private JSON review report containing source keys, revision ids, proposed values and ambiguities. Normal stdout remains aggregate-only.
- Never modify originals, rewrite EXIF, hydrate source files, or infer a Place from image content.

## Rule file shape

```json
{
  "inferDateFromFilename": true,
  "inferDateFromDirectory": true,
  "excludedDateDirectories": ["1970"],
  "placeRules": [
    {
      "name": "example-trip",
      "from": "2024-07-06",
      "to": "2024-07-20",
      "place": "Spain/Canary Islands/Tenerife"
    }
  ]
}
```

The Place value uses the same normal UI hierarchy accepted by the existing Place repository; callers do not need to add the reserved `Places/` prefix.

## Operator workflow

Preview first:

```powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- metadata enrich `
  --postgres-connection-env PHOTOIDENTITY_POSTGRES `
  --rules .\metadata-rules.json `
  --report .\artifacts\metadata-enrichment-dry-run.json
```

After reviewing the private report, apply the same rules explicitly:

```powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- metadata enrich `
  --postgres-connection-env PHOTOIDENTITY_POSTGRES `
  --rules .\metadata-rules.json `
  --report .\artifacts\metadata-enrichment-apply.json `
  --apply
```

A rerun should be safe: already-effective values are not proposed, and repository writes are append-only/idempotent at their existing boundaries.

## Acceptance criteria

- [ ] `metadata enrich` is dry-run unless `--apply` is supplied.
- [ ] The command scans only current image revisions and reports aggregate missing/proposed/ambiguous counts without printing private paths by default.
- [ ] Existing effective capture dates are never replaced by filename/directory inference.
- [ ] Existing named Places and valid non-zero GPS locations are not replaced by date-range Place rules.
- [ ] `1970/...` is excluded from directory-date inference by default, while a plausible timestamp encoded in a filename can still supply a date.
- [ ] Filename dates that disagree with a non-excluded `YYYY/MM` directory are reported as ambiguous rather than written.
- [ ] Folder-only inference stores `YYYY-MM` precision rather than inventing a day.
- [ ] Date-range Place rules require full containment of the photo's effective precision range.
- [ ] Conflicting matching Place rules are ambiguous and do not write a Place.
- [ ] Apply uses the existing PostgreSQL capture-date and Place repositories and rechecks current state before each write.
- [ ] Optional private report is sufficient to inspect proposed revision/source/value changes before apply.
- [ ] Automated tests cover filename, directory, catch-all, conflict and precision-containment behavior.
- [ ] Maintainer verifies a production-catalogue dry-run before any broad apply operation.

## Verification plan

1. Run focused integration tests for `MetadataEnrichmentPlannerTests` and the ordinary repository validation suite.
2. Run documentation validation/generation checks.
3. On the maintainer catalogue, create a rule file with no Place rules and run dry-run to compare missing-date counts with the known baseline.
4. Review a sample of exact filename proposals, month-directory proposals, `1970` timestamp proposals and ambiguities in the private report.
5. Add one known trip rule, dry-run it, and confirm that exact dates inside the trip are proposed while partial month/year precision that extends outside the trip is not.
6. Only after the dry-run is accepted, use `--apply`, then rerun dry-run and confirm the applied rows are no longer proposed.

## Privacy and safety notes

The rule and report files can contain private archive paths and travel/location history. They are operator-local artifacts and must not be committed. CLI stdout intentionally exposes only aggregate counts. Source originals remain read-only throughout this workflow.
