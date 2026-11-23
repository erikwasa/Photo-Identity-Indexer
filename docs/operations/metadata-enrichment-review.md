# Metadata enrichment review

WI-0163 dry-run reports can contain hundreds of photo-level proposals. Review them by rule first rather than reading every source path.

## Summarize a dry run

~~~powershell
.\review-metadata-enrichment.ps1 `
  -Report .\artifacts\wi-0163-place-tier1-dry-run.json
~~~

The helper prints the catalogue summary and one row per place rule with:

- proposal count;
- effective date;
- proposed Place;
- top-level source roots represented in the rule;
- two source-key samples by default.

Use `-SamplesPerRule 0` for summary only, or a larger value when a rule needs closer inspection.

## Produce an approved rule set

To approve every rule represented by the reviewed report:

~~~powershell
.\review-metadata-enrichment.ps1 `
  -Report .\artifacts\wi-0163-place-tier1-dry-run.json `
  -Rules .\metadata-place-rules-tier1.json `
  -ApproveAll `
  -ApprovedRulesOutput .\artifacts\wi-0163-place-tier1-approved.json
~~~

To approve only selected rules, repeat `-ApproveRule` through an array:

~~~powershell
.\review-metadata-enrichment.ps1 `
  -Report .\artifacts\wi-0163-place-tier1-dry-run.json `
  -Rules .\metadata-place-rules-tier1.json `
  -ApproveRule @(
    'example-rule-a',
    'example-rule-b'
  ) `
  -ApprovedRulesOutput .\artifacts\wi-0163-place-selected-approved.json
~~~

The generated file disables date inference and contains only the selected place rules. The helper never writes catalogue metadata.

## Apply only after review

Use the generated approved file with the existing explicit apply gate:

~~~powershell
 dotnet run --project src/PhotoIdentity.Cli -c Release -- metadata enrich `
   --postgres-connection-env PHOTOIDENTITY_POSTGRES_CONNECTION_STRING `
   --rules .\artifacts\wi-0163-place-tier1-approved.json `
   --report .\artifacts\wi-0163-place-tier1-apply.json `
   --apply
~~~

Then rerun the same command without `--apply` to verify that the approved proposals have disappeared. Originals, EXIF and GPS are never modified; accepted proposals create append-only named Place actions only.

## Source-scoped Place rules

A Place rule can optionally include an archive-relative `sourcePrefix`. Use it for imported folders or capture streams where a date-based location decision should not affect unrelated cameras or people elsewhere in the archive.

```json
{
  "inferDateFromFilename": false,
  "inferDateFromDirectory": false,
  "excludedDateDirectories": ["1970"],
  "placeRules": [
    {
      "name": "example-import-2020-01-01",
      "sourcePrefix": "example-import/",
      "from": "2020-01-01",
      "to": "2020-01-01",
      "place": "Country/Region/City/Example Place"
    }
  ]
}
```

This rule can propose a Place only for currently unlocated photos whose source key is under `example-import/` and whose entire effective capture-date range is inside 2020-01-01. A photo under `2020/01/` on the same date is not affected.

`sourcePrefix` uses folder-boundary semantics. `example-import`, `example-import/` and `EXAMPLE-IMPORT\\` normalize to the same scope; `example-import-backup/` is not inside that scope. `.` and `..` path segments are rejected.

Rules without `sourcePrefix` keep the archive-wide behavior. Existing named Places and valid non-zero GPS are never replaced by these proposals. Dry-run remains the default and catalogue writes still require `metadata enrich --apply`. When `review-metadata-enrichment.ps1` writes an approved subset of rules, `sourcePrefix` is retained because the selected rule objects are copied intact.
