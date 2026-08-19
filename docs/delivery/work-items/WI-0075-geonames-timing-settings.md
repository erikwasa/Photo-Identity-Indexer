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

The API already binds:

- `PhotoIdentity:GeoNames:AutomaticEnrichmentEnabled`;
- `PhotoIdentity:GeoNames:AutomaticMinimumRequestIntervalMilliseconds`;
- `PhotoIdentity:GeoNames:AutomaticIdlePollIntervalMilliseconds`.

However, `Start-PhotoIdentity.ps1` has an explicit `$SupportedSettings` allow-list and does **not** currently include the three automatic-enrichment keys. Supplying them in the launcher settings file therefore fails as unsupported.

The automatic request interval also has a hard safe floor of 30,000 ms. Values above the floor are already meaningful in API configuration; values below it are clamped. M20 must make the launcher behavior and safe-override semantics explicit rather than leaving the setting half-exposed.

## Contract

- Add the automatic GeoNames settings to the launcher allow-list and packaged launcher behavior.
- Document the environment/launcher names:
  - `PhotoIdentity__GeoNames__AutomaticEnrichmentEnabled`;
  - `PhotoIdentity__GeoNames__AutomaticMinimumRequestIntervalMilliseconds`;
  - `PhotoIdentity__GeoNames__AutomaticIdlePollIntervalMilliseconds`.
- Document units, defaults and validation ranges.
- Keep 30 seconds as the conservative unattended default.
- Decide and implement explicit semantics for an operator requesting a value below the current 30-second safe floor. Do not silently pretend the requested value was applied if it is actually clamped.
- Surface the effective automatic request interval and idle poll interval in Settings/diagnostics so the operator can confirm what the process is using.
- Preserve provider backoff behavior for quota/transport failures; ordinary timing settings must not disable provider-directed backoff.

## Acceptance criteria

- [ ] All automatic GeoNames keys are accepted in `PhotoIdentity.launcher.json` and passed to the API process.
- [ ] Launcher example/operator documentation contains the keys and units.
- [ ] Effective values appear in Settings/diagnostics.
- [ ] A configured longer request interval is honored across restart.
- [ ] A configured idle poll interval within the supported range is honored across restart.
- [ ] Below-safe-floor behavior is explicit and tested: either a validated opt-in override or a clear configuration error/warning, never silent clamping without operator visibility.
- [ ] Provider quota/backoff responses still take precedence over normal pacing.
- [ ] Launcher/package verification covers the newly accepted settings.
