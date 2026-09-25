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
    '2024-03-23-sodermalm',
    '2024-03-24-sodermalm'
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
