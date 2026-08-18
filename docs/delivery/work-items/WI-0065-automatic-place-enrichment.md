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

Remove GeoNames reverse geocoding from the normal manual/operator workflow. Once a private GeoNames username is configured, Photo Identity should continuously enrich eligible GPS-bearing revisions in the server process without requiring the browser to keep a long HTTP request open and without requiring the maintainer to calculate provider limits or repeatedly start batches.

Automatic enrichment must remain resumable from durable catalogue state, must never block archive/local analysis on a network service, and must preserve all WI-0064 privacy, manual-place precedence, cache and failure semantics.

## Motivation

WI-0064 proved the live provider path and fixed valid provider hierarchies longer than the ordinary tag limit. During final live verification, a 200-candidate manual run with a one-second configured provider interval hit the Blazor/HTTP client timeout at roughly 100 seconds. The photos processed before cancellation were persisted successfully, while later photos simply remained eligible for another run.

That exposed a design mismatch rather than a catalogue failure: large reverse-geocoding queues are long-running, externally rate-limited work and should not be tied to one browser HTTP request.

The maintainer also confirmed that the previously failing long-place revisions were automatically retryable and all succeeded after PR #165. WI-0065 therefore changes the operating model rather than reopening the long-path fix.

## Provider budget decision

As of 2026-08-18, the GeoNames free web-service documentation states a 10,000-credit daily limit and 1,000-credit hourly limit per username/application. The GeoNames credit table lists `findNearbyPlaceName` at 3 credits per request.

References:

- https://www.geonames.org/export/
- https://www.geonames.org/export/credits.html

Normal automatic operation must not require the maintainer to reason about those numbers. The first implementation slice therefore imposes a conservative **30-second minimum between actual automatic GeoNames requests**, independent of any lower raw provider-client interval configured for maintenance/testing. At continuous operation that is at most 120 requests/hour (360 credits) and 2,880 requests/day (8,640 credits), leaving headroom below both documented free-service limits.

Provider quota/availability responses continue to pause the worker and are retried automatically with bounded backoff. Future provider tiers may make the automatic budget configurable upward, but lowering automatic pacing below the safe floor is intentionally not a normal operator setting.

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
        -> worker continues later at provider-safe pacing
```

No archive-specific queue handoff is required. `photo_capture_metadata` is the durable boundary: as soon as GPS is present in SQLite, the existing WI-0064 candidate query makes that revision eligible. This keeps reverse geocoding independent of original-file availability and means closing/restarting the application simply resumes from existing attempt/cache state.

## Slice 1 — automatic hosted worker and operator status

Implement:

- a `PhotoPlaceEnrichmentHostedService` that continuously drains the existing normal GeoNames candidate queue one revision at a time;
- automatic activation when GeoNames is configured, with an optional local `PhotoIdentity:GeoNames:AutomaticEnrichmentEnabled=false` escape hatch;
- a 30-second automatic provider-request floor that cannot be reduced by the lower-level `MinimumRequestIntervalMilliseconds` setting;
- fast continuation for cache hits/manual-protected/conflict rows because they do not spend provider credits;
- automatic retry/backoff for provider quota, overload, transport and authorization stop states;
- no browser/request cancellation token as the lifetime owner of catalogue-wide enrichment;
- Settings status showing automatic worker state, last activity, next attempt and automatic pacing;
- retain small manual maintenance and force-refresh controls for diagnostics only, with the browser-facing maintenance batch deliberately capped well below the previous 250-candidate workflow;
- no new original-file reads or hydration.

## Follow-up hardening

Before final completion, evaluate whether to add a durable provider-credit ledger. The 30-second floor is sufficient for unattended free-tier operation by itself, but a durable ledger would also account for unusual concurrent manual calls and repeated process restarts when enforcing provider budgets.

If implemented, the ledger must remain provider-neutral enough to support changed GeoNames limits or a premium tier without changing catalogue Place semantics.

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
- [ ] Normal automatic operation enforces provider-safe pacing without requiring the maintainer to calculate hourly/daily limits.
- [ ] GeoNames quota, overload and transport stop states pause/retry automatically rather than requiring repeated manual batches.
- [ ] Closing/restarting Photo Identity resumes outstanding enrichment from durable catalogue state.
- [ ] Settings clearly reports automatic worker state and explains that normal enrichment no longer requires a manual batch.
- [ ] Manual maintenance/force-refresh remains available for focused diagnostics and intentional automatic-place refresh.
- [ ] Manual Place/manual-clear precedence remains intact.
- [ ] Automated coverage verifies automatic activation/disable behavior, provider-request pacing and resumable worker cycles without live GeoNames calls.
- [ ] A maintainer local pass confirms that adding/processing a GPS photo eventually produces the expected Place without pressing the maintenance button.

## Verification notes

The WI-0064 live-provider verification already proves the configured GeoNames account, HTTPS provider path, canonical Place normalization, Smart Collection integration and long hierarchy storage. WI-0065 verification should therefore focus on orchestration: automatic pickup, safe pacing, restart/resume behavior and independence from the browser request lifetime.
