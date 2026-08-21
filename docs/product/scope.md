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
6. A local SQLite catalogue with stable source, asset, revision, face and person identity plus append-only review history.
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

## Optional or deferred

These are not required for the local product or for version 1:

- Azure execution and checkpointing;
- Microsoft Graph access to Personal OneDrive;
- Azure application identities or managed identities;
- public hosting or a cloud database;
- GPU requirements;
- neural-network fine-tuning; and
- video processing.

Azure remains an optional compute path for bounded jobs. The canonical catalogue, review workflow and source-of-truth decisions remain local.