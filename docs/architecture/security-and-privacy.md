# Security and privacy

Face crops, embeddings and identity labels are sensitive biometric data.

Requirements:

- Personal OneDrive authentication remains inside the official Windows client.
- No OneDrive password or personal account token enters the application or Azure.
- Azure receives only explicit bundles; full originals are uploaded only when required.
- Prefer crop-only bundles for embedding comparisons.
- Protect bundles during transfer and keep temporary storage private.
- Short-lived SAS credentials, when used, must be narrowly scoped and never logged.
- Protect SSH keys locally.
- Delete temporary Azure inputs after verified result import.
- Keep the canonical SQLite database local and backed up.
- Use internal IDs rather than person names in logs where practical.
- Do not publicly expose the review API during early versions.
- Never modify original photos.

## Source-copy privacy exclusion

ADR-0008 defines **Exclude from Photo Identity** as a source-copy privacy boundary rather than a normal visibility filter.

- Exclusion is scoped to one source plus normalized source key/path.
- Exact duplicate content at another path is not excluded automatically.
- Exclusion does not follow a rename/move; a newly observed destination path is independently controlled.
- The same excluded source locator remains blocked across restart/rescan until the operator explicitly re-includes it.
- Exclusion becomes durable and blocks media/processing access before purge begins.
- Purge removes locally retained photo derivatives, revision hashes/metadata, faces, embeddings and photo-linked identity/review data.
- Only the minimal source-locator tombstone and purge operational state are retained after purge.
- The source/OneDrive original is never modified or deleted by exclusion.
- Failed/incomplete cleanup must remain retryable while access stays blocked.

This privacy purge is an intentional exception to normal append-only retention of canonical review history for the excluded photo. Shared people and unrelated evidence remain intact after all links from the excluded photo are removed.

See [Source-copy lifecycle and privacy exclusion](../product/source-copy-lifecycle.md) and [ADR-0008](../decisions/ADR-0008-source-copy-exclusion-and-purge.md).
