---
id: WI-0065
title: Run GeoNames place enrichment automatically in the background
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0050, WI-0063, WI-0064]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite]
---

# WI-0065: Run GeoNames place enrichment automatically in the background

## Objective

Remove GeoNames reverse geocoding from the normal manual/operator workflow. Once a private GeoNames username is configured, Photo Identity should continuously enrich eligible GPS-bearing revisions in the server process without requiring the browser to keep a long HTTP request open and without requiring the maintainer to repeatedly start batches.

Automatic enrichment must remain resumable from durable catalogue state, must never block archive/local analysis on a network service, and must preserve all WI-0064 privacy, manual-place precedence, cache and failure semantics.

## Motivation

WI-0064 proved the live provider path and fixed valid provider hierarchies longer than the ordinary tag limit. During final live verification, a 200-candidate manual run with a one-second configured provider interval hit the Blazor/HTTP client timeout at roughly 100 seconds. The photos processed before cancellation were persisted successfully, while later photos simply remained eligible for another run.

That exposed a design mismatch rather than a catalogue failure: large reverse-geocoding queues are long-running, externally rate-limited work and should not be tied to one browser HTTP request.

The maintainer also confirmed that the previously failing long-place revisions were automatically retryable and all succeeded after PR #165. WI-0065 therefore changes the operating model rather than reopening the long-path fix.

## Provider pacing decision

The first implementation used a conservative **30-second hard minimum** between automatic GeoNames requests so unattended free-tier operation stayed well below the provider credit limits.

The maintainer review on 2026-08-21 supersedes that policy:

- **30 seconds remains the default automatic request interval.**
- It is **not** a mandatory minimum.
- An explicitly configured lower non-negative interval is an operator override and must be honored rather than silently clamped or rejected solely for being below 30 seconds.
- Settings/diagnostics must show the effective normal pacing actually used by the worker.
- The automatic worker and lower-level GeoNames client must not apply contradictory independent defaults that make the reported override ineffective.
- GeoNames quota/account/transport responses remain authoritative: provider-directed backoff may pause the worker longer than the configured normal interval.
- Documentation should warn that aggressive values can consume provider credits quickly, while preserving operator control.

The launcher-facing correction is tracked in WI-0075.

## Architecture

Automatic GeoNames enrichment is a server-side hosted service, parallel to other Photo Identity background workers. It is **not** another synchronous stage inside archive face analysis.

The flow is:

```text
archive/local ingestion or metadata backfill
        -> capture metadata persisted
        -> GPS present
        -> revision becomes eligible in the existing GeoNames queue
        -> automatic hosted service selects one eligible revision
        -> cache/manual/conflict rules run
        -> GeoNames request only when needed
        -> first-class Place is assigned
        -> worker continues later at configured/provider-safe pacing
```

No archive-specific queue handoff is required. `photo_capture_metadata` is the durable boundary: as soon as GPS is present in SQLite, the existing WI-0064 candidate query makes that revision eligible. This keeps reverse geocoding independent of original-file availability and means closing/restarting the application simply resumes from existing attempt/cache state.

## Automatic hosted worker and operator status

The implementation provides:

- a `PhotoPlaceEnrichmentHostedService` that continuously drains the existing normal GeoNames candidate queue one revision at a time;
- automatic activation when GeoNames is configured, with an optional local `PhotoIdentity:GeoNames:AutomaticEnrichmentEnabled=false` escape hatch;
- configurable normal automatic request pacing with a conservative 30-second default;
- fast continuation for cache hits/manual-protected/conflict rows because they do not spend provider credits;
- automatic retry/backoff for provider quota, overload, transport and authorization stop states;
- no browser/request cancellation token as the lifetime owner of catalogue-wide enrichment;
- Settings status showing automatic worker state, last activity, next attempt and automatic pacing;
- small manual maintenance and force-refresh controls for diagnostics only;
- no new original-file reads or hydration.

## GeoNames language policy correction — 2026-08-21

The maintainer verified that `lang=local` produces the desired Swedish names for Swedish photos but undesirable local-language names for some photos outside Sweden.

Desired policy:

- for Sweden (`countryCode=SE`), retain GeoNames local-language names;
- outside Sweden, assign the English GeoNames representation;
- if the provider contract cannot support that policy reliably, the fallback preference is Swedish globally rather than arbitrary local languages.

Preferred provider workflow:

1. Query `lang=local` first so Swedish coordinates keep Swedish names without an unnecessary second request.
2. If the result country is not `SE`, resolve/cache an English (`lang=en`) representation before assigning the automatic Place.
3. Provider/cache contract keys must include the effective language policy so old local-language cached results are not mistaken for results produced under the new policy.
4. A foreign coordinate that already has the English result cached under the current contract must not repeatedly incur two live requests.
5. Preserve manual-place/manual-clear precedence, migration-conflict protection, privacy, no-hydration and provider-backoff behavior.
6. Settings/operator documentation must state that the first lookup of a non-Swedish coordinate can consume an additional provider request.

The provider-client normalization/cache details are owned jointly with WI-0064. Consolidated review notes are in `../milestones/M20-maintainer-review-2026-08-21.md`.

## Privacy and precedence

Configuring a private GeoNames username remains the explicit opt-in to external reverse geocoding. Once configured, automatic operation may send persisted latitude/longitude without another click for each photo. Settings must continue to state this clearly.

The worker must preserve WI-0064 protections:

- never send photo bytes, filenames, people, tags or private source paths to GeoNames;
- never open/hydrate originals for reverse geocoding;
- never overwrite a current manual Place or explicit manual clear;
- never overwrite unresolved migration conflicts;
- terminal no-result rows remain complete for the current provider contract;
- failed/deferred rows remain retryable;
- identical coordinates may reuse the existing provider cache;
- force refresh may replace automatic results but not manual ones.

## Acceptance criteria

- [ ] With a configured GeoNames username, automatic enrichment starts without pressing an Enrich button.
- [ ] Newly persisted GPS metadata becomes eligible automatically whether it came from archive processing, metadata backfill or another supported catalogue path.
- [ ] Archive analysis does not wait for GeoNames and GeoNames never hydrates/open originals.
- [ ] Unattempted, failed and deferred revisions resume automatically from existing SQLite state.
- [ ] Completed success/no-result/manual-protected/conflict rows are not repeatedly sent to GeoNames.
- [ ] Normal automatic operation uses the configured non-negative request interval with a 30000 ms default and no hidden 30000 ms floor.
- [ ] GeoNames quota, overload and transport stop states pause/retry automatically and can override the normal interval with longer provider backoff.
- [ ] Closing/restarting Photo Identity resumes outstanding enrichment from durable catalogue state.
- [ ] Settings clearly reports automatic worker state, effective pacing and explains that normal enrichment no longer requires a manual batch.
- [ ] Manual maintenance/force-refresh remains available for focused diagnostics and intentional automatic-place refresh.
- [ ] Manual Place/manual-clear precedence remains intact.
- [ ] Sweden uses local-language GeoNames place names while non-Swedish results use English, with cache keys/reuse matching the new language policy.
- [ ] Automated coverage verifies automatic activation/disable behavior, configured pacing, resumable worker cycles and the Sweden-local/else-English language policy without live GeoNames calls.
- [ ] A maintainer local pass confirms automatic pickup, restart/resume, an explicit below-30-second pacing override, and representative Swedish/non-Swedish place naming.

## Verification notes

The WI-0064 live-provider verification already proves the configured GeoNames account, HTTPS provider path, canonical Place normalization, Smart Collection integration and long hierarchy storage. Remaining verification should focus on automatic orchestration, effective configurable pacing, restart/resume behavior, browser-lifetime independence and the revised language policy.
