For a metadata enrichment dry-run report, review Place proposals by rule instead of printing every photo:

  .\review-metadata-enrichment.ps1 -Report .\artifacts\wi-0163-place-tier1-dry-run.json

To approve all represented rules into a write-ready rule file without touching the catalogue:

  .\review-metadata-enrichment.ps1 `
    -Report .\artifacts\wi-0163-place-tier1-dry-run.json `
    -Rules .\metadata-place-rules-tier1.json `
    -ApproveAll `
    -ApprovedRulesOutput .\artifacts\wi-0163-place-tier1-approved.json

See docs/operations/metadata-enrichment-review.md.
