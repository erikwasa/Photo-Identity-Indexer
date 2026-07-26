# Build context

## Current milestone

**M04 — Minimal review application**

## Current work item

**WI-0015 — Build minimal review application**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0015-device-verification`
- Draft pull request: [#28 — Add device verification harness for review app](https://github.com/erikwasa/Photo-Identity-Indexer/pull/28)

## Objective

Complete the Windows and Pixel acceptance path for the merged local ASP.NET Core and Blazor WebAssembly review application without using private photos or risking changes to a real catalogue.

## Current slice

Add a repeatable verification harness. A fixture tool creates an isolated SQLite catalogue with synthetic coloured crops and seeded unreviewed, assigned and rejected queues. `verify-review.ps1` starts the real hosted application, checks the privacy and mutation boundaries, prints localhost and LAN URLs and leaves the process running during the manual device review.

## Relevant files

- `tools/PhotoIdentity.ReviewVerification/PhotoIdentity.ReviewVerification.csproj`
- `tools/PhotoIdentity.ReviewVerification/Program.cs`
- `verify-review.ps1`
- `.github/workflows/build.yml`
- `src/PhotoIdentity.Api/Program.cs`
- `src/PhotoIdentity.Api/ReviewEndpoints.cs`
- `src/PhotoIdentity.Web/Pages/Home.razor`
- `src/PhotoIdentity.Web/Pages/FaceDetails.razor`
- `src/PhotoIdentity.Web/wwwroot/css/app.css`
- `docs/delivery/work-items/WI-0015-review-ui.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
./verify-review.ps1
./verify-review.ps1 -Mode Smoke -Configuration Release
./verify-review.ps1 -Mode Prepare -Configuration Release

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- The generated catalogue and crops exist only below `.artifacts/review-verification`.
- The harness never opens or changes a configured real catalogue.
- The real hosted API returns all synthetic faces and opaque image URLs.
- Review JSON and image responses include `Cache-Control: no-store`.
- Person creation, assignment and undo succeed and retain audit history.
- The API process is stopped after Smoke mode and after Interactive mode exits.
- No Windows Firewall rule is created automatically.
- The script prints useful localhost and LAN URLs.
- A human confirms the gallery, actions, details navigation and touch targets on Windows and Pixel.

## Verification

Pull request #27 merged at `88f5c2c1b2dbccea9e99870405bbb9e280aa1d00`. GitHub Actions run `30189387917` passed dependency audit, Release build, all automated tests, schema migrations, path-redaction and cache-control coverage, documentation checks and Windows mixed-media verification. The merged implementation has no review comments or unresolved threads.

Draft pull request #28 adds the disposable verification path and a Windows PowerShell smoke gate. WI-0015 remains open because merge evidence does not establish the target-device criterion.

## Known issues

- The local trusted-network slice has no authentication and uses unencrypted HTTP.
- LAN access depends on the selected Windows Firewall network profile; the harness deliberately does not change it.
- Pixel PWA installation requires a secure context, although the responsive review workflow can be exercised over trusted-network HTTP.
- Automated browser/API checks cannot prove that touch controls are comfortable on the actual Pixel.

## Next action

Resolve CI or review findings on pull request #28. After merge, run `./verify-review.ps1` on Windows, complete the printed Pixel checklist and report that verification before marking WI-0015 and M04 completed or starting WI-0016.
