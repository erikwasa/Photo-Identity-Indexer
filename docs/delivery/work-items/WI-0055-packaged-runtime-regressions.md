---
id: WI-0055
title: Fix packaged review, archive and storage-policy regressions
milestone: M18
status_source: ../status/work-items.yaml
depends_on: [WI-0042, WI-0048, WI-0052, WI-0054]
related_adrs: []
affected_modules: [packaging/windows, PhotoIdentity.Api, PhotoIdentity.Web, archive-hydration, review-proxies]
---

# WI-0055: Fix packaged review, archive and storage-policy regressions

## Objective

Restore the accepted review-image and archive behavior when Photo Identity is started from the packaged Windows launcher with durable operator data outside the package, and make the effective bounded-hydration policy visible and actionable from the web application.

## Why

Real packaged-app verification after the M18 packaging work exposed three connected operator regressions:

1. Face Gallery and Face Details still present pixelated 112x112 review images even though human review should use a higher-resolution proxy/original-derived crop and the 112x112 image is only appropriate for recognition embedding input.
2. A previously analyzed permanent archive is no longer represented as analyzed, archive advancement is blocked by a request to configure `PhotoIdentity__RepositoryRoot` to a source checkout, and Library preview messaging can contradict the reported full-resolution state for a local revision-verified original.
3. The bounded archive-hydration controls `MinimumFreeSpaceReserveBytes`, `MaximumManagedHydrationBytes`, and `MaximumConcurrentOperations` are not visible in Settings, while the packaged launcher example does not configure them. The runtime currently disables managed hydration until all three are configured.

These failures undermine the packaged application's operator boundary: routine use should not require a repository checkout, accepted archive state should survive packaging and upgrades, and safety-critical storage policy should be inspectable without reverse-engineering environment-variable names.

## In scope

- Reproduce and fix the packaged-app path that still serves or displays intrinsic 112x112 face-review images after the high-resolution review-image change.
- Preserve 112x112 model/embedding crops for recognition while ensuring human-review surfaces prefer a materially higher-resolution review proxy or original-derived crop when one is available.
- Make archive analysis/advancement resolve all required runtime resources from the package and durable operator configuration without requiring `PhotoIdentity__RepositoryRoot` to point to a source checkout.
- Reconcile existing archive-analysis state from the configured permanent catalogue and archive-analysis root so previously completed analysis is recognized rather than shown as unknown solely because package/repository-root resolution changed.
- Correct Library preview availability and messaging so a local, revision-verified original can supply the accepted safe normal-view fallback without implicit hydration, and UI messages do not contradict the reported original-access state.
- Surface the effective archive-hydration policy in the web Settings experience, including current values, units, whether the policy is fully configured/enabled, configuration source where practical, and whether a restart is required for changes.
- Provide a supported operator path for changing `PhotoIdentity:ArchiveHydration:MinimumFreeSpaceReserveBytes`, `PhotoIdentity:ArchiveHydration:MaximumManagedHydrationBytes`, and `PhotoIdentity:ArchiveHydration:MaximumConcurrentOperations`. If these remain startup-only settings, Settings must clearly show them as read-only and identify the exact launcher keys and restart requirement rather than implying they can be changed live.
- Update packaged launcher examples/operator documentation so the nested environment keys are discoverable:
  - `PhotoIdentity__ArchiveHydration__MinimumFreeSpaceReserveBytes`
  - `PhotoIdentity__ArchiveHydration__MaximumManagedHydrationBytes`
  - `PhotoIdentity__ArchiveHydration__MaximumConcurrentOperations`
- Add regression coverage for the packaged runtime boundary, existing archive-state reuse, review-image dimensions/source selection, storage-policy visibility, and local revision-verified preview fallback.

## Out of scope

- Changing the recognition model, SFace embedding dimensions, or the model's 112x112 recognition input.
- Re-analyzing the maintained archive merely to repair status presentation or package resource resolution.
- Automatically hydrating online-only originals during normal browsing.
- Removing bounded hydration, free-space reserve, managed-byte or concurrency safeguards.
- A broad redesign of Settings beyond the archive-hydration policy needed to make these controls visible and operable.
- Moving the permanent catalogue, archive-analysis output or review proxies into the application package directory.

## Acceptance criteria

- [ ] With the current packaged build and a configured durable `ReviewProxyRoot`, Face Gallery and Face Details use a human-review image whose source dimensions materially exceed 112x112 whenever a suitable review proxy/original-derived source is available; automated evidence proves the 112x112 recognition crop is not being scaled as the preferred human-review source.
- [ ] The recognition/embedding pipeline continues to use its required model input and produces unchanged recognition behavior; the review-image fix does not alter model semantics.
- [ ] Starting `PhotoIdentity.cmd` from an extracted Windows operator package with durable `DatabasePath`, `ArchiveAnalysisOutputRoot`, and `ReviewProxyRoot` does not require a Photo Identity source checkout or `PhotoIdentity__RepositoryRoot` for normal archive status, advancement, proxy generation, or Library review-preview behavior.
- [ ] Given an existing permanent catalogue and archive-analysis output containing completed work, Folder status reports meaningful Analyzed/Pending/Failed counts and unchanged completed items remain reusable; packaging/resource resolution alone cannot turn previously analyzed coverage into unknown `—` state.
- [ ] Archive advancement is available when its actual package/runtime prerequisites and hydration policy are satisfied, and any blocking message names the missing operator-controlled prerequisite rather than directing the user to a repository checkout.
- [ ] For an original that is already local and revision-verified, normal Library viewing can use the accepted safe preview fallback when no durable proxy exists, without requiring the explicit full-resolution hydration action and without contradictory availability text.
- [ ] Normal Library browsing still never hydrates an online-only authoritative original implicitly; explicit full-resolution access and archive processing retain the existing bounded-storage rules.
- [ ] Settings shows the effective values and configured/enabled status of `MinimumFreeSpaceReserveBytes`, `MaximumManagedHydrationBytes`, and `MaximumConcurrentOperations`, with byte values presented in operator-readable units while preserving the exact configured values.
- [ ] Settings provides either a durable supported edit/save path for all three hydration controls or a clear startup-only/read-only presentation that identifies the exact `PhotoIdentity__ArchiveHydration__...` launcher keys and restart requirement.
- [ ] The packaged launcher example and Windows operator documentation include the three archive-hydration keys and explain that managed hydration is disabled until all three valid values are configured.
- [ ] Automated tests cover package-independent runtime-resource resolution, existing analysis-state recognition, high-resolution review-image selection, hydration-policy visibility/configuration state, and local revision-verified preview fallback; human Windows verification covers the reported packaged scenario against a non-destructive copy or the maintained durable catalogue configuration.

## Verification requirements

Automated regression tests are required for resource/configuration resolution and the affected API/UI state contracts. Human verification is also required on Windows by launching the packaged application through `PhotoIdentity.cmd` with durable data paths outside the package, checking Face Gallery and Face Details image quality/intrinsic dimensions, Archive folder counts and advancement availability, Library preview behavior for both local and online-only originals, and the Settings hydration-policy presentation.

Verification must preserve the maintained archive and originals: do not force mass re-analysis or uncontrolled hydration as part of the test.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
