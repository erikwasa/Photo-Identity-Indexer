---
id: WI-0167
title: Fail best-quality slideshow preparation when OneDrive is unavailable
milestone: M32
status_source: ../status/work-items.yaml
depends_on: [WI-0093, WI-0096]
related_adrs: [ADR-0007]
affected_modules: [PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0167: Fail best-quality slideshow preparation when OneDrive is unavailable

## Objective

Make best-quality slideshow preparation fail promptly and actionably when it needs an online-only original but the Windows OneDrive sync client is not available to perform the requested hydration.

The normal UI may continue to show the simple `x / total ready` progress treatment. The defect is not missing fine-grained counters; it is that preparation can remain at `0 / N` for an extended period after Windows accepts a Files On-Demand pin request even though OneDrive is closed and no process is available to download the file.

## Maintainer evidence

On a trusted-LAN phone, starting a slideshow with **Prepare originals** enabled remained indefinitely at `0 / N` while OneDrive was closed on the archive PC. Starting OneDrive caused the already-requested originals to begin downloading and the slideshow then started normally.

## Scope

- Detect or otherwise establish a bounded, privacy-safe signal that OneDrive is unavailable when Photo Identity actually needs to hydrate an online-only original.
- Stop the affected best-quality preparation session with an actionable message such as: `OneDrive is not available. Start OneDrive on this computer, then retry.`
- Do not make already-local slideshow playback depend on OneDrive process availability.
- Distinguish an unavailable sync client from a legitimate slow/active OneDrive download. The existing delayed no-progress warning remains appropriate for the latter.
- Keep the existing immutable slideshow snapshot and storage-budget/concurrency guarantees.
- Make Retry after OneDrive is started genuinely reconcile/reassert Photo-Identity-owned hydration where necessary rather than only resetting the no-progress timer.
- Do not use Microsoft Graph; personal OneDrive remains accessed through the Windows sync client.
- Keep errors path-free and suitable for a remote phone UI.

## Acceptance criteria

- [ ] A slideshow whose required originals are already local can prepare and play normally while OneDrive is not running.
- [ ] When an online-only original is required and OneDrive is unavailable, preparation reaches an actionable failure/recovery state within a bounded interval rather than waiting indefinitely at `0 / N`.
- [ ] An active but slow OneDrive hydration is not misclassified merely because the ready count is temporarily unchanged.
- [ ] The failure message tells the maintainer to start OneDrive and retry without exposing source paths.
- [ ] Retry after OneDrive becomes available reuses the same immutable snapshot and can resume/reassert Photo-Identity-owned hydration without exceeding the configured storage or concurrency policy.
- [ ] Automated tests cover already-local behavior, unavailable-client behavior, slow/downloading behavior and retry recovery.

## Verification plan

1. With OneDrive running, prepare a collection containing at least one online-only original and confirm normal best-quality preparation still succeeds.
2. Make a required original online-only, stop OneDrive, start preparation from a phone and confirm an actionable OneDrive-unavailable state appears within the bounded detection interval.
3. Start OneDrive and use Retry; confirm the same slideshow snapshot hydrates and becomes playable.
4. Repeat with all required originals already local while OneDrive is stopped; confirm playback is unaffected.
5. Exercise a deliberately slow active hydration and confirm the generic no-progress recovery remains distinct from the unavailable-client failure.
