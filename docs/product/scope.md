# Product scope

Photo Identity Indexer is a private, local-first system for building and using a person/photo index over a personal archive.

## Version 1 scope

Version 1 establishes the permanent catalogue and proves that full-archive ingestion can begin safely on the Windows control plane.

It includes:

1. A stable permanent archive root backed by the local OneDrive synchronisation client.
2. Incremental recursive folder coverage under that root, including resynchronisation of previously included folders.
3. Explicit source availability and verification state for local and Files On-Demand content.
4. Decode support for the image formats needed by the archive, including HEIC/HEIF and the RAW variants found during archive inventory.
5. The governed CenterFace face-detection pipeline, SFace alignment and model-versioned embeddings.
6. A local PostgreSQL catalogue with stable source, asset, revision, face and person identity plus append-only review history.
7. A local browser application for face review, people maintenance, progress inspection and collection browsing.
8. Identity suggestions with exact-model provenance and durable negative evidence.
9. Bounded OneDrive hydration for analysis and explicit full-resolution viewing.
10. Durable local review proxies so routine browsing does not require original-photo hydration.
11. Resumable processing, completeness reporting, backup and restore.
12. Read-only treatment of original photos.

Version 1 is complete when these capabilities are sufficient to start the permanent catalogue from the real archive. It does not require the entire archive to finish processing before the version is considered successful.

## Planned after version 1

The accepted roadmap then improves the permanent catalogue rather than replacing it:

- complete full-archive coverage and add ongoing synchronisation;
- add configurable High/Medium/Low identity confidence groups and optional canonical High-confidence automatic assignment;
- add favorite people, Unknown review state and browser-triggered suggestion regeneration;
- simplify the application around Review and Library and improve Windows startup/packaging;
- extract EXIF capture metadata and location for smart collections;
- add protected fullscreen slideshows from saved Smart Collections, including toddler-resistant phone playback and optional bounded original preparation; and
- experiment with local visible-content tagging before selecting a production semantic-tagging approach.

## Video support after version 1 (M30, active development)

The [M30 video milestone](../delivery/milestones/M30-video-media-support.md)
has already delivered the opt-in video catalogue and technical metadata,
durable Rec.709 still posters, bounded local playable proxies, selected
timestamped frame caption/embedding evidence and separate Video Search.
The existing photo catalogue, photo search, collections and slideshow
behavior are preserved by default. Those completed capabilities apply
to an explicitly bounded initial MOV/MP4 subset, not arbitrary video
formats or whole-archive online-only hydration.

**Next user-facing feature:** play a current retained local proxy in
the browser using the existing trusted-LAN HTTPS phone access where
configured (WI-0152). A playable proxy can be served even when its
original is online-only; playback never silently recalls the original.

**Next coverage phases:** one archive-evidence-selected compatibility
expansion (WI-0210), then explicitly opted-in photo-style managed
OneDrive video hydration/reuse/release (WI-0207). Basic metadata of
interest includes trustworthy capture date/time, GPS/Place and optional
altitude, camera make/model, duration, resolution and FPS. Mixed
collections are wanted later but deferred; video-enabled Smart
Collection predicates require a distinct future decision.

**Video face recognition is a desired future feature**, initially
explored in a bounded prototype (WI-0155) without modifying the
canonical production catalogue. A complex tracker/review system
is not promised before that experiment. Live Photo pairing is
not a current product goal (WI-0156 retired).

See [video compatibility by stage](../operations/video-compatibility-matrix.md).

## Optional or deferred

These are not prerequisites to ordinary photo or basic video
operation:

- Azure VM/checkpoint orchestration, public cloud hosting or a cloud
  copy of the canonical database.
- Microsoft Graph or Personal OneDrive application credentials.
- Required GPU hardware or neural-model fine-tuning.
- Universal proprietary video decoding, HDR support without
  reviewed tone mapping, automatic mass transcoding or compulsory
  entire-video downloads for browsing.
- Advanced video Smart Collection filtering, video slideshows,
  Live Photo pairing and complete face-tracking machinery.
- Performance, storage and cost benchmarking beyond evidence needed
  for a real observed problem or a deliberately expanded workflow.

## Local-first and optional Azure inference

Windows remains the canonical control plane for PostgreSQL, OneDrive
hydration, person identity governance, exclusions and publication.
The accepted [ADR-0013](../decisions/ADR-0013-optional-operator-controlled-remote-compute.md)
permits explicitly selected private photo/video-frame inference on
a vetted **maintainer-controlled Azure application and model** as an
optional accelerator. Local processing remains the default; neither
mandatory cloud infrastructure nor unvetted black-box third-party
AI services are part of the product.
