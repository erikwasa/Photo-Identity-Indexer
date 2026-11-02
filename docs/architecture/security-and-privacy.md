# Security and privacy

Face crops, embeddings and identity labels are sensitive biometric data. Photos and video frames can also contain private content.

Requirements:

- Personal OneDrive authentication remains inside the official Windows client.
- No OneDrive password or personal account token enters the application or Azure.
- Azure may receive only explicitly operator-authorized, purpose-scoped input to vetted, maintainer-operated application/model endpoints (including bounded photo thumbnails or video frames). **Do not use unvetted third-party black-box AI/vision services, including Microsoft/Azure managed inference APIs.** Review runtime dependencies, model weights and platform privacy before use.
- Original photos/videos, face data, paths, location/identity metadata or whole archives are not implicitly transferred; additional sensitive input requires a separately approved task-specific need and retention policy. Verify source exclusion and revision before export and before local publication.
- Prefer crop-only bundles for embedding comparisons.
- Protect remote inputs during authenticated encrypted transport and keep temporary storage private; restrict endpoint/network access, minimize platform/runtime logs and define remote retention, deletion and scale-to-zero controls.
- Short-lived SAS credentials, when used, must be narrowly scoped and never logged.
- Protect SSH keys locally.
- Delete temporary Azure inputs after verified result import.
- Keep the canonical PostgreSQL database local/private and covered by the documented backup/restore procedure.
- Use internal IDs rather than person names in logs where practical.
- Do not publicly expose the review API during early versions.
- Never modify original photos.

## Pragmatic local-first and optional Azure/video policy (2026-11-02)

These controls distinguish **important privacy boundaries** from
unnecessary new infrastructure or test campaigns:

- The security goal is not “never leave the local computer”.
  [ADR-0013](../decisions/ADR-0013-optional-operator-controlled-remote-compute.md)
  permits explicit inference using a **known operator-managed
  application/model** on Azure. Do not use an unvetted hosted
  black-box AI service. A reviewed existing bridge may be reused;
  there is no requirement to rebuild a separate service, obtain
  redundant per-video approvals or repeat a full infrastructure
  evaluation for each harmless request.
- Keep authorization for **what leaves the computer** clear:
  the operator chooses whether to enable a task; send only what
  that chosen model requires; do not export OneDrive credentials,
  canonical database access, unrelated private media or identity
  authority. Use authenticated encrypted transport and sensible
  private temporary-data handling. Evaluate additional remote
  data/retention risks when a materially new task or provider is added.
- Excluded media and out-of-date revisions never become available
  through source reads, published derivatives, video search or
  playback. Preserve original-file read-only semantics, private
  derived data and local catalogue/assignment authority.
- For video processing, **reuse managed OneDrive hydration ownership**
  from photos: an app-hydrated original may stay local while active
  or already planned processing needs it; release only eligible
  app-owned downloads after that work and its leases end.
  Never automatically unpin a user-local or user-pinned original.
  An available, authorized retained proxy may play without opening
  its online-only original. Mere search/playback must not hydrate.
- Baseline code/revision/exclusion/integrity tests plus one real
  authorized media example are generally adequate for an already
  proven workflow. Mandatory destructive cloud-outage, kernel-sandbox,
  exhaustive performance or disaster exercises need a specific
  observed risk or separately approved objective.

This interpretation **does not remove** HTTPS/authentication,
secret protection, exclusion-purge obligations, process safeguards,
private-data handling or the established PostgreSQL backup/restore
plan. Keep those controls; avoid new requirements that do not address
a concrete failure mode.

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
