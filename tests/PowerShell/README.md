# PowerShell smoke checks

`review-metadata-enrichment.smoke.ps1` exercises WI-0163's grouped review helper against temporary synthetic report/rule files. Run it from any working directory with PowerShell:

```powershell
pwsh -NoProfile -File .\tests\PowerShell\review-metadata-enrichment.smoke.ps1
```

`package-postgres-startup-retry.smoke.ps1` is Windows-specific. It copies the packaged `PhotoIdentity.cmd` entry point into a temporary fixture, simulates one PostgreSQL `57P03` startup failure followed by success, and verifies that an unrelated PostgreSQL failure is not retried:

```powershell
pwsh -NoProfile -File .\tests\PowerShell\package-postgres-startup-retry.smoke.ps1
```

WI-0208 restores use two additional **source-free tests**. They never start Podman or access a real database: the first exercises ownership/cleanup guards and atomic report writes, and the second invokes the real restore verifier against an in-process fake Podman boundary (success, retention, resume, duplicate retry, interruption, failure and protected cleanup):

~~~powershell
pwsh -NoProfile -File .\tests\PowerShell\postgres-restore-lifecycle.tests.ps1
pwsh -NoProfile -File .\tests\PowerShell\postgres-restore-verifier.integration.ps1
~~~

A separate **opt-in real Podman/PostgreSQL fixture** uses a new randomly named Compose project and synthetic schema, and requires `-ConfirmDisposableService`:

~~~powershell
pwsh -NoProfile -File .\tests\PowerShell\postgres-restore-disposable.integration.ps1 -ConfirmDisposableService
~~~

Real Windows/Podman acceptance is documented separately in [the WI-0208 operator procedure](../../docs/operations/postgres-wi0208-verification.md).
