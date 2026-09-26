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
- Allow a Place rule to include an optional archive-relative `sourcePrefix`, such as `fideli/`, so a date/location rule can be restricted to one imported folder or capture stream instead of affecting unrelated photos from the same date.
- Normalize `sourcePrefix` separators and folder boundaries; `fideli` and `fideli/` mean the same folder scope, while `fideli-backup/` does not match. Parent-directory (`..`) traversal is rejected.
- Apply a Place rule only when the photo's entire effective date range is contained inside the rule and, when present, the photo's source key is inside the rule's `sourcePrefix`. A month- or year-precision value that merely overlaps a trip is ambiguous rather than a match.
- Do not replace an existing effective capture date, named Place, or valid non-zero GPS location by default.
- Recheck current state immediately before apply so changes made after a dry-run are not overwritten.
- Persist accepted changes through the existing append-only capture-date and Place repositories so provenance, hierarchy creation and idempotence remain consistent with UI edits.
- Optionally write a local private JSON review report containing source keys, revision ids, proposed values and ambiguities. Normal stdout remains aggregate-only.
- Provide a local review helper that collapses photo-level Place proposals into one row per rule, shows a small sample, and can write an approved subset of rules without touching the catalogue.
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
    },
    {
      "name": "fideli-hospital-day",
      "sourcePrefix": "fideli/",
      "from": "2024-03-23",
      "to": "2024-03-23",
      "place": "Sverige/Stockholms län/Stockholms stad/Södermalm/Södersjukhuset"
    }
  ]
}
```

The Place value uses the same normal UI hierarchy accepted by the existing Place repository; callers do not need to add the reserved `Places/` prefix. `sourcePrefix` is optional. Omitting it preserves the original archive-wide date-rule behavior.

## Operator workflow

Preview first:

```powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- metadata enrich `
  --postgres-connection-env PHOTOIDENTITY_POSTGRES `
  --rules .\metadata-rules.json `
  --report .\artifacts\metadata-enrichment-dry-run.json
```

For Place-heavy reports, review grouped decisions instead of every individual source path:

```powershell
.\review-metadata-enrichment.ps1 `
  -Report .\artifacts\metadata-enrichment-dry-run.json
```

To write a reviewed rule file containing every rule represented by that report:

```powershell
.\review-metadata-enrichment.ps1 `
  -Report .\artifacts\metadata-enrichment-dry-run.json `
  -Rules .\metadata-rules.json `
  -ApproveAll `
  -ApprovedRulesOutput .\artifacts\metadata-enrichment-approved.json
```

Use `-ApproveRule @(...)` instead of `-ApproveAll` to select only specific groups. The helper is review-only: it never writes catalogue metadata. Scoped rule properties such as `sourcePrefix` are preserved in the approved rule file. See `docs/operations/metadata-enrichment-review.md` for the full workflow.

After reviewing the private report or grouped approved rules, apply explicitly:

```powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- metadata enrich `
  --postgres-connection-env PHOTOIDENTITY_POSTGRES `
  --rules .\artifacts\metadata-enrichment-approved.json `
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
- [ ] A Place rule with `sourcePrefix` affects only source keys under that folder boundary; similarly named sibling folders remain unaffected.
- [ ] Different source-scoped Place rules can safely assign different Places for the same date without conflicting when their source scopes do not overlap.
- [ ] Conflicting matching Place rules are ambiguous and do not write a Place.
- [ ] Apply uses the existing PostgreSQL capture-date and Place repositories and rechecks current state before each write.
- [ ] Optional private report is sufficient to inspect proposed revision/source/value changes before apply.
- [ ] Place-heavy private reports can be summarized and reduced to an approved rule subset without catalogue writes.
- [ ] Automated tests cover filename, directory, catch-all, conflict, precision-containment and source-prefix behavior.
- [ ] Maintainer verifies a production-catalogue dry-run before any broad apply operation.

## Verification plan

1. Run focused integration tests for `MetadataEnrichmentPlannerTests`, `MetadataEnrichmentSourcePrefixTests` and the ordinary repository validation suite.
2. Run documentation validation/generation checks.
3. On the maintainer catalogue, create a rule file with no Place rules and run dry-run to compare missing-date counts with the known baseline.
4. Review a sample of exact filename proposals, month-directory proposals, `1970` timestamp proposals and ambiguities in the private report.
5. For Place rules, run `review-metadata-enrichment.ps1` and confirm the grouped counts match the photo-level report before approving all or selected rules.
6. Add a `sourcePrefix` rule for a known folder/date and confirm the dry-run proposes only matching source keys while same-date photos outside that folder remain untouched.
7. Add one known trip rule, dry-run it, and confirm that exact dates inside the trip are proposed while partial month/year precision that extends outside the trip is not.
8. Only after the dry-run is accepted, use `--apply`, then rerun dry-run and confirm the applied rows are no longer proposed.

## Privacy and safety notes

The rule and report files can contain private archive paths and travel/location history. They are operator-local artifacts and must not be committed. CLI stdout intentionally exposes only aggregate counts. The grouped review helper reads only the local report/rules files and writes only a new local approved-rules file when requested. Source originals remain read-only throughout this workflow.
