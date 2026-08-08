# Review-proxy measurement

Use this procedure for the WI-0042 proxy-size measurement gate. The command generates private local derivatives and prints aggregate storage statistics only. It does not select or register a permanent catalogue proxy default.

## Safety boundary

- Keep the source corpus private and unchanged.
- Put the measurement output outside the source/OneDrive archive root. The CLI rejects an output directory nested under the supplied source root so generated proxies cannot be discovered as source assets.
- Do not commit generated proxies, private filenames, screenshots, pixels or identity data.
- Record only aggregate storage results and the exact profile settings needed by WI-0042.

## 100-image tuning sample

Use the unchanged private 100-image detector-evaluation set. Run at least two candidates so storage and visual usability can be compared before choosing a permanent profile.

```powershell
$Sample = "C:\path\to\private-100-image-sample"
$Output = "C:\path\outside\OneDrive\proxy-measurement-100"

dotnet run --project src/PhotoIdentity.Cli -- archive proxy measure `
  --source $Sample `
  --output $Output `
  --profile jpeg-1600-q78:1600:78 `
  --profile jpeg-2048-q82:2048:82
```

Candidate syntax is `ID:MAX_LONG_EDGE:JPEG_QUALITY`. Profile IDs are durable identities: if settings change, use a different ID. The initial `1600/78` and `2048/82` values above are measurement candidates, not approved production defaults.

For every profile the report includes the exact protocol/settings, total proxy bytes, mean/median/p95 proxy bytes and source-to-proxy compression ratio. The source image count and total logical source bytes are reported once for the corpus. No source filenames are written to the report.

Inspect a representative subset under each profile directory for normal whole-photo browsing and identity-review context. This is a display-quality check only; canonical detector analysis continues to use the authoritative original.

## 560-image scale validation

After selecting one candidate from the 100-image sample, run that exact profile by itself against the unchanged private 560-image pilot corpus. The command intentionally supports a single profile for this stage.

```powershell
$Sample = "C:\path\to\private-560-image-pilot"
$Output = "C:\path\outside\OneDrive\proxy-measurement-560"

dotnet run --project src/PhotoIdentity.Cli -- archive proxy measure `
  --source $Sample `
  --output $Output `
  --profile <SELECTED_ID>:<MAX_LONG_EDGE>:<JPEG_QUALITY>
```

Retain only the privacy-safe aggregate report in repository evidence. WI-0042 should freeze the permanent proxy default only after the 100-image visual/storage choice and the 560-image scale estimate are complete.
