# WI-0151 — read-only video library and verified poster delivery

The video library is separate from existing photo search, face review, Smart/Creative Collections and slideshows. It displays only current non-excluded video catalogue revisions, their duration/dimensions/capture date and a verified SDR still poster when available. It **does not** play media, hydrate OneDrive files, perform analysis, or expose embedded GPS or camera tags.

## Default-off activation (deferred maintainer acceptance)

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

## Not yet WI-0151 completion

The separate page intentionally does not merge video cards into photo galleries or collections, which remain photo-only. Remaining work includes real-file HDR poster validation/explicit color profiles, server-side photo-only query audits, complete end-to-end privacy/photo regression tests and the maintainer's combined acceptance after all implementation checkpoints are merged.

No automatic background video indexing or general player has been enabled.
