# Source-scoped Place rules

WI-0163 Place rules can optionally include an archive-relative `sourcePrefix`. This is intended for imported folders or capture streams where a date-based location decision should not affect unrelated cameras or people elsewhere in the archive.

Example:

```json
{
  "inferDateFromFilename": false,
  "inferDateFromDirectory": false,
  "excludedDateDirectories": ["1970"],
  "placeRules": [
    {
      "name": "fideli-sodersjukhuset-2024-03-23",
      "sourcePrefix": "fideli/",
      "from": "2024-03-23",
      "to": "2024-03-23",
      "place": "Sverige/Stockholms län/Stockholms stad/Södermalm/Södersjukhuset"
    }
  ]
}
```

The rule above can propose a Place only for currently unlocated photos whose source key is under `fideli/` and whose entire effective capture-date range is inside 2024-03-23. A photo under `2024/03/` on the same date is not affected.

`sourcePrefix` uses folder-boundary semantics. `fideli`, `fideli/` and `FIDELI\\` normalize to the same scope; `fideli-backup/` is not inside that scope. `.` and `..` path segments are rejected.

Rules without `sourcePrefix` keep the existing archive-wide behavior. Existing named Places and valid non-zero GPS are never replaced by these proposals. Dry-run remains the default and catalogue writes still require `metadata enrich --apply`.

Use `review-metadata-enrichment.ps1` on the dry-run report to review grouped decisions. If the helper writes an approved subset of rules, `sourcePrefix` is retained because the selected rule objects are copied intact.
