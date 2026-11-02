---
id: WI-0152
title: Add safe browser video serving and viewer playback
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151, WI-0189]
related_adrs: [ADR-0012]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0152: Add safe browser video serving and viewer playback

## Priority and objective

**Completed and maintainer-accepted (2026-11-03; merged PR #720).**
Play an already-produced, current locally retained WI-0189 proxy in the
existing browser viewer on Windows and phone, with play/pause/seek and
a useful missing-proxy state. Build on the already completed poster,
proxy and trusted-LAN infrastructure instead of a new streaming stack.

## In scope — pragmatic first slice

- Serve a verified, current, revision/exclusion-authorized playable
  H.264/AAC MP4 **proxy** through an existing guarded same-origin API.
  Implement HTTP byte-range and seek support as required by browser
  playback; never expose absolute cache paths.
- Add familiar play, pause, seek and duration controls; preserve the
  existing image viewer and photo-only flows.
- **A retained proxy may be played even when its original is OneDrive
  online-only.** Serving it must not hydrate/open the original.
- A missing, evicted or invalid proxy displays a clear unavailable /
  prepare-later indication. Browser play/seek does **not** auto-hydrate,
  auto-transcode, start a video job or silently fall back to an original.
  Any later explicit prepare action must use the existing guarded
  processing/hydration workflow (WI-0207), not a new mechanism.
- Reuse the established optional **trusted-LAN HTTPS** mobile path
  (WI-0082), configured by the operator. Preserve loopback-only
  desktop defaults; do not invent separate phone authentication or
  a public media server.

## Out of scope

- Automatic online-only retrieval, direct original-file streaming,
  runtime transcoding, new cloud services or streaming infrastructure.
- Slideshow integration (WI-0154), face processing, collection filters,
  bulk video proxy generation and additional codec support (WI-0210).
- Mandatory cross-browser/device laboratory, HDR quality approval,
  physical original availability for an already-valid proxy.

## Acceptance criteria

- [x] On the maintained local host, an already-retained supported proxy
  plays, pauses and seeks correctly in the browser with no original
  hydration or new video processing job.
- [x] Current revision, exclusion, file integrity and safe-range guards
  prevent serving unauthorized, stale or malformed proxy bytes.
- [x] An online-only original with a retained proxy is play-capable
  without hydration; an unavailable proxy fails clearly and safely.
- [x] Existing image viewer remains unchanged.
- [x] A real phone can play/seek using existing opt-in trusted-LAN
  HTTPS if that established mobile path is configured and available;
  no new LAN security or identity system is required.

## Verification requirements

Focused range/authorization tests and one maintained desktop playback
check. Use the existing phone/HTTPS setup for a simple real-phone
check where available; if the device path is unavailable, record
precisely what remains unverified instead of engineering new infrastructure.
No private video content is committed.

## Completion notes

- **Status: all five scoped acceptance criteria verified on the
  maintained host on 2026-11-03.** PR #719 merged the scope decision;
  PR #720 implemented and merged the feature. The repository lifecycle
  is `completed` and archived.
- Checkpoint 1 (2026-11-02) adds a separate GET/HEAD
  `/api/videos/{revisionId}/play` endpoint, SHA/byte-length verified
  bounded MP4 streams, ASP.NET byte-range 206/416 semantics, source-row
  locks held through the HTTP response, refreshed revision/exclusion gates,
  canonical app-owned cache enforcement, and the opt-in Videos page's
  embedded browser controls and missing-proxy indication.
- Playback only reads registered succeeded PlayableProxy outputs using
  the approved standard-720p and compact-540p versions. It never touches
  an original file, sends cache paths, hydrates OneDrive, or enqueues jobs.
- **Maintainer-reported Windows acceptance, 2026-11-03:** focused
  PostgreSQL and HTTP range tests PASS; docs validate and generate PASS;
  desktop play/pause/seek, HTTP 206/416/404, no new processing, retained
  proxy with OneDrive-online-only original remaining online-only, existing
  HTTPS phone playback, and photo viewer/search regression all PASS at
  the functional level. These are operator-observed results, not an
  independent automated device lab, and contain no private media evidence.
- **Visual regression found and resolved:** the initial host run
  exposed clipping of the lower video/player controls on both PC and phone.
  The follow-up CSS/markup correction in PR #720 eliminated the fixed
  poster-frame constraint. **The maintainer confirmed on 2026-11-03
  that the player is fully visible on both devices** after the fix.
  The earlier functional PASS results remain valid; this second
  report closes the outstanding visual acceptance blocker.
- **Checkpoint 2:** PR #720 additionally changes the active card to
  use a full-width row, releases the fixed 16:10 poster-frame clip
  while playing, uses natural video element height with a 75dvh cap,
  and retains `object-fit: contain`. The added
  `VideoPlaybackLayoutContractTests` style contract guards the
  CSS clipping. The maintainer's post-fix real PC and phone visual
  acceptance is **PASS**, independent of the static style contract.
- Follow the
  [WI-0152 playback acceptance runbook](../../operations/video-playback-wi0152.md)
  for repeatable local verification. Maintainer-reported functional
  playback, original hydration safeguards, photo regression and the
  post-fix desktop/phone full-frame view all PASS. The Windows GitHub
  Actions build for follow-up commit `fe24b58` passed six jobs:
  build/tests, two integration shards, launcher, package and
  PR classification. The new documentation/status commits still
  were merged with PR #720 after review.
- **No outstanding scope acceptance blockers.** PR #720 merged on
  2026-11-03; WI-0152 is completed and archived after maintainer
  acceptance, without re-running the already accepted real-media tests.
- Keep acceptance bounded to locally retained playable proxies.
