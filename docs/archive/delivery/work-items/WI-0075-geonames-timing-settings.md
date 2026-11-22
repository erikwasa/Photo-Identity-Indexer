---
id: WI-0075
title: Make GeoNames background timing configurable from launcher settings
milestone: M20
status_source: ../status/work-items.yaml
depends_on: [WI-0065]
related_adrs: []
affected_modules: [PhotoIdentity.Api, launcher, documentation]
---

# WI-0075: Make GeoNames background timing configurable from launcher settings

## Objective

Allow the operator to control automatic GeoNames background timing through the same `PhotoIdentity.launcher.json` settings mechanism used for the rest of local application configuration.

## Current state

The API binds:

- `PhotoIdentity:GeoNames:AutomaticEnrichmentEnabled`;
- `PhotoIdentity:GeoNames:AutomaticMinimumRequestIntervalMilliseconds`;
- `PhotoIdentity:GeoNames:AutomaticIdlePollIntervalMilliseconds`.

PR #198 exposed the corresponding launcher settings and implemented a conservative 30-second hard floor for unattended request pacing.

The 2026-08-21 maintainer review accepted the launcher integration but rejected the hard-floor policy. The 30-second value should be the **default**, not a mandatory minimum. An explicit lower non-negative value should override that default.

## Revised contract — 2026-08-21

- Keep the automatic GeoNames settings in the launcher allow-list and packaged launcher behavior.
- Document the environment/launcher names:
  - `PhotoIdentity__GeoNames__AutomaticEnrichmentEnabled`;
  - `PhotoIdentity__GeoNames__AutomaticMinimumRequestIntervalMilliseconds`;
  - `PhotoIdentity__GeoNames__AutomaticIdlePollIntervalMilliseconds`.
- Keep **30000 ms** as the conservative default automatic request interval.
- Do **not** enforce 30000 ms as a minimum. Explicit lower non-negative values must be accepted and applied as requested.
- Use `0` to represent no intentional normal automatic pacing delay if the provider client supports that value.
- Validate only the actual supported numeric range; do not silently clamp a lower requested value back to 30000 ms.
- Surface the effective automatic request interval and idle poll interval in Settings/diagnostics so the operator can confirm what the process is using.
- Reconcile the automatic worker interval with the lower-level GeoNames client pacing so a requested automatic interval is not silently defeated by a separate default throttle. The effective value reported to the operator must match actual normal pacing.
- Preserve provider-directed quota/account/transport backoff. Backoff may delay a retry longer than the configured normal request interval.
- Operator documentation may warn that aggressive intervals can spend GeoNames credits quickly, but the application should not reject an explicit lower value merely because it is below the conservative default.

## Maintainer review — 2026-08-21

Launcher propagation, restart behavior and diagnostics were verified successfully. The only requested correction is the pacing-policy change above.

The original PR #198 behavior that rejects values below 30000 ms is therefore superseded and must be removed in the corrective implementation slice.

See `../milestones/M20-maintainer-review-2026-08-21.md` for the consolidated review.

## Acceptance criteria

- [x] All automatic GeoNames keys are accepted in `PhotoIdentity.launcher.json` and passed to the API process.
- [x] Launcher example/operator documentation contains the keys and units.
- [x] Effective values appear in Settings/diagnostics.
- [x] A configured longer request interval is honored across restart.
- [x] A configured idle poll interval within the supported range is honored across restart.
- [ ] 30000 ms is the default but not a minimum; explicitly configured lower non-negative values are accepted and applied without silent clamping.
- [ ] Lower-level provider-client pacing does not silently override the effective automatic interval reported to the operator.
- [ ] Provider quota/backoff responses still take precedence over normal pacing.
- [ ] Launcher/package verification is updated to cover a below-30000 explicit override rather than expecting startup rejection.
- [ ] Final maintainer verification confirms the configured lower value reaches the running worker unchanged.
