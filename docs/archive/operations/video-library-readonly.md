# WI-0151 — read-only video library and verified poster delivery

The video library is separate from existing photo search, face review, Smart/Creative Collections and slideshows. It displays only current non-excluded video catalogue revisions, their duration/dimensions/capture date and a verified SDR still poster when available. It **does not** play media, hydrate OneDrive files, perform analysis, or expose embedded GPS or camera tags.

## Default-off activation (maintainer acceptance completed)

The default launcher keeps this route disabled. For WI-0151's **consolidated real-file acceptance**, first verify that source-copy exclusion/purge uses the exact same poster root as the operator's poster generator, then configure the maintained local API:

- PhotoIdentity:VideoLibraryEnabled = true (environment: PhotoIdentity__VideoLibraryEnabled=true)
- PhotoIdentity:VideoPosterRoot = <absolute approved derivative root> (environment: PhotoIdentity__VideoPosterRoot=<path>)

The default poster root is LOCALAPPDATA/PhotoIdentity/video-derivatives, matching the operator command. A nondefault root must match the configured source-copy purge root exactly. Never point derivatives into the archive originals. Restart the API after configuration changes.

The maintained Windows launcher now accepts these settings under the `settings` object of your private `PhotoIdentity.launcher.json`: `PhotoIdentity__VideoLibraryEnabled` (explicit Boolean string) and `PhotoIdentity__VideoPosterRoot` (existing absolute local directory, no symlinks or junctions). When enabling the library, the launcher **requires** the explicit root; it validates the configuration before the API starts. Sample launcher JSON intentionally leaves videos disabled. For exact backup, dry-run, write, browser validation and post-review shutdown steps, use the [consolidated WI-0151 acceptance procedure](video-wi0151-consolidated-acceptance.md).

The opt-in advanced navigation entry is Advanced → Archive operations → Videos (opt-in). The page shows a disabled-state message when the server feature is not enabled.

HTTP GET /api/videos?offset=0&limit=24 provides bounded paging (limit 1–60), with current video revision, inspection status, capture date, duration, dimensions, rotation and poster presence only. No source IDs, roots, source-relative filenames, camera make/model, GPS or photo-derived fields are returned.

Each poster uses GET /api/videos/{revisionId}/poster. The repository checks source-copy tombstones, asset soft deletion, exact current video revision, supported inspection and explicit bt709 transfer. The server holds the PostgreSQL source row lock while verifying and reading the immutable poster image into a bounded byte array; concurrent exclusion waits until that authorized read completes.

A poster must be under the configured independent derivative root with a canonical revision/profile/tool-derived path, no reparse-point ancestors, maximum 8 MiB size, valid JPEG boundary markers and SHA-256 matching its PostgreSQL evidence. Missing/unsafe/corrupted files produce no image; no filesystem path or exception details are returned to the browser. Both video API routes use no-store responses.

This checkpoint does not require source original bytes to display a poster and never opens a OneDrive placeholder. Unsupported/unknown video metadata is displayed as an explicit status; no fabricated date or video still is returned.

## Final WI-0151 status — accepted 2026-10-19

The maintainer completed the one-time consolidated local acceptance with **11/11 required checks passing**. The enabled read-only video gallery displayed expected metadata/poster behavior; the video API responded HTTP 404 with the feature disabled before and after the test. Exactly one previously created durable SDR poster was visually accepted. The final bounded materialization run reported one existing eligible poster, no new poster, seven HDR candidates deferred and zero changes/failures. Poster reads remained revision-bound and exclusion-aware. The maintained photo Archive, search, collections, slideshows and original files were unchanged.

**WI-0151 is completed for its original SDR still-poster and opt-in video-library scope.** This page remains separate from photo galleries and does not provide playable video, automatic video indexing, HDR/PQ production stills, search embeddings, slideshow clips or video frame analysis. Those are separate future work items and must not be inferred from this acceptance. The exact launcher/poster-root consistency and no-hydration safety requirements above still apply to subsequent operator use.
