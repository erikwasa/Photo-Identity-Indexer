# Success criteria

## Version 1

Version 1 is reached when Photo Identity Indexer is safe and practical to **begin building the permanent catalogue directly from the full personal photo archive**.

Version 1 does not require every archive image to have finished processing. Full-archive completion is ongoing operational work after the permanent catalogue has been proven ready to start.

The version-1 gate is satisfied when:

- One stable permanent archive source can be configured and expanded incrementally by relative folder without creating duplicate source identities.
- Adding broader folder coverage discovers newly eligible images while unchanged images and previously completed exact-profile analysis are reused.
- Archive synchronisation distinguishes verified, unverified, changed, unavailable, unsupported, failed and completed states instead of silently omitting files.
- The image pipeline supports the photo formats needed by the real archive, including JPEG/PNG plus HEIC/HEIF and the RAW variants actually present in the archive.
- The governed permanent analysis profile uses the selected CenterFace detector pipeline and exact SFace embedder provenance.
- Interrupted synchronisation, hydration and analysis can resume without duplicating assets, immutable revisions, face occurrences or embeddings.
- A successful zero-face result is durable and is not repeatedly reprocessed.
- OneDrive Files On-Demand can be used without keeping the complete logical archive hydrated locally.
- Normal browsing and identity-review context can use durable local review proxies while authoritative originals remain in OneDrive.
- Full-resolution originals can be hydrated explicitly when needed, verified against the immutable revision and released again under configured storage limits.
- Existing canonical people, identity assignments, rejections and append-only review history remain usable as permanent archive coverage grows.
- Original photos remain read-only and are never modified by catalogue creation, review, tagging or metadata extraction.
- The SQLite catalogue and its governed local artefacts can be backed up, restored and integrity-checked before permanent processing begins.
- The operator documentation provides a clear path from a clean checkout to configuring, synchronising and advancing the permanent archive.

The concrete version-1 readiness work is represented by the permanent-ingestion, bounded-storage and archive-format work in M12. Once those gates pass, the first real archive folders may be added to the permanent catalogue and processing can continue incrementally from there.

## Beyond version 1

The following are valuable planned capabilities but are not required to declare version 1 archive-ready:

- completing processing of every eligible archive asset;
- ongoing automatic synchronisation after the initial permanent catalogue is established;
- configurable confidence groups and canonical automatic identity assignment;
- favorite people, Unknown review state and web-based match regeneration;
- simplified Review/Library navigation and packaged Windows startup;
- EXIF/location smart collections and visible-content tagging; and
- optional Azure scale-out or later production-model experiments.

Trust remains more important than raw automation rate. Automatic identity assignment is an accepted future direction, but it must be explicit, configurable, auditable and correctable rather than silently changing historical decisions.