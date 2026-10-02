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

## Implementation

The Windows Files On-Demand adapter now exposes a bounded sync-client availability signal. A confirmed absence of the `OneDrive` desktop process is `Unavailable`; process-enumeration failures are `Unknown` and preserve the existing behavior, while process presence is only `Available` and is not treated as proof that synchronization is healthy.

Best-quality preparation consults that signal only when hydration is actually required. Already-local originals continue through normal immutable verification without depending on OneDrive process availability. An online-only original with a confirmed unavailable client enters an explicit `onedrive-unavailable` recovery phase immediately rather than waiting for the generic no-progress threshold.

The recovery keeps the same preparation session and immutable revision set alive. After OneDrive is started, **Retry preparation** rechecks that same snapshot and, when Photo Identity already owns an in-flight managed hydration, reasserts the Files On-Demand pin once. Externally pinned/local content is never claimed or reasserted by this path. Active/unknown-client downloads retain the existing two-minute no-progress recovery rather than being misclassified as unavailable.

Both the slideshow player and slideshow-library preparation tools render the recovery message and Retry action without exposing a source path.

## Acceptance criteria

- [x] A slideshow whose required originals are already local can prepare and play normally while OneDrive is not running.
- [x] When an online-only original is required and OneDrive is unavailable, preparation reaches an actionable failure/recovery state within a bounded interval rather than waiting indefinitely at `0 / N`.
- [x] An active but slow OneDrive hydration is not misclassified merely because the ready count is temporarily unchanged.
- [x] The failure message tells the maintainer to start OneDrive and retry without exposing source paths.
- [x] Retry after OneDrive becomes available reuses the same immutable snapshot and can resume/reassert Photo-Identity-owned hydration without exceeding the configured storage or concurrency policy.
- [x] Automated tests cover already-local behavior, unavailable-client behavior, slow/downloading behavior and retry recovery.

## Automated coverage added

- Source-level coverage protects the bounded process-presence classification used by the Windows adapter.
- Slideshow preparation coverage verifies that already-local originals remain usable with the client unavailable.
- Slideshow preparation coverage verifies online-only fail-fast recovery, actionable path-free messaging, same-session Retry and successful hydration after the client becomes available.
- Slideshow preparation coverage verifies the harder case where Photo Identity already owns a `Downloading` hydration when OneDrive stops; Retry reasserts that app-owned hydration once and completes the same session.
- Existing no-progress coverage remains responsible for a client that is present/unknown but slow or stuck, preserving the distinction between unavailable and merely not progressing.
- Web preparation-experience coverage protects immediate parent attention for retryable OneDrive-unavailable state without setting the generic no-progress flag.

## Verification plan

1. With OneDrive running, prepare a collection containing at least one online-only original and confirm normal best-quality preparation still succeeds.
2. Make a required original online-only, stop OneDrive, start preparation from a phone and confirm an actionable OneDrive-unavailable state appears within the bounded detection interval.
3. Start OneDrive and use Retry; confirm the same slideshow snapshot hydrates and becomes playable.
4. Repeat with all required originals already local while OneDrive is stopped; confirm playback is unaffected.
5. Exercise a deliberately slow active hydration and confirm the generic no-progress recovery remains distinct from the unavailable-client failure.

## Verification status

Implementation and automated coverage are present on the WI-0167 branch. The acceptance checkboxes intentionally remain open until CI passes and the Windows/phone verification plan above is exercised on the maintained archive PC.

### Maintainer verification update — 2026-09-30 (Europe/Stockholm)

Maintainer explicitly reports **not tested yet**. Keep `in_review`; the unavailable-client / Retry / already-local-original checks remain outstanding. The attached log and other slideshow observations do not substitute for these checks.

## Maintainer acceptance — 2026-10-02 (Europe/Stockholm)

The maintainer confirms WI-0167 works as expected. The prior not-tested note is superseded by this maintained acceptance. Canonical lifecycle is completed, verified by erikwasa. This acceptance supersedes earlier outstanding-verification statements above.
