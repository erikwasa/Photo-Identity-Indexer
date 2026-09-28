# PowerShell smoke checks

`review-metadata-enrichment.smoke.ps1` exercises WI-0163's grouped review helper against temporary synthetic report/rule files. Run it from any working directory with PowerShell:

```powershell
pwsh -NoProfile -File .\tests\PowerShell\review-metadata-enrichment.smoke.ps1
```

`package-postgres-startup-retry.smoke.ps1` is Windows-specific. It copies the packaged `PhotoIdentity.cmd` entry point into a temporary fixture, simulates one PostgreSQL `57P03` startup failure followed by success, and verifies that an unrelated PostgreSQL failure is not retried:

```powershell
pwsh -NoProfile -File .\tests\PowerShell\package-postgres-startup-retry.smoke.ps1
```
