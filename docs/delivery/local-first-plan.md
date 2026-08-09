# Local-first delivery strategy

Local-first is now a durable product strategy, not a temporary workaround for unavailable Azure resources.

The Windows computer is the trusted control plane. It owns the canonical SQLite catalogue, human and automatic review history, archive configuration, private source paths and long-lived derivatives. Personal OneDrive is accessed through the official sync client. Azure, when used, receives explicit bounded bundles and is never required to reach version 1.

## Current delivery sequence

### 1. Make the permanent archive safe to start

The immediate M12 goal is archive readiness:

- WI-0042 proves bounded hydration, source re-verification, durable review proxies and real-machine storage policy.
- WI-0041 completes the stable permanent archive root, incremental relative coverage and no-repeat exact-profile analysis workflow once WI-0042 is accepted.
- WI-0053 adds HEIC/HEIF and the RAW variants actually present in the archive so format gaps do not become silent permanent omissions.

When those gates pass, the project reaches the version-1 success point: the permanent catalogue can begin processing the real full archive incrementally.

### 2. Grow the permanent catalogue without restarting it

After version 1, new folders and broader parent coverage are added to the same stable source identity. Every synchronization revisits included coverage for new, changed, missing or newly available files while unchanged completed revisions are reused.

The full archive does not need to be hydrated at once. Normal review remains proxy-backed; authoritative originals are materialized only for analysis or explicit full-resolution viewing and are governed by local storage limits.

### 3. Complete archive coverage and ongoing synchronization

WI-0023 remains the broader full-archive completion item. Its production-model/Azure dependencies apply to declaring the complete archive processed; they do not prevent starting the permanent catalogue with the currently governed archive analysis profile.

M13 then turns the permanent catalogue into an ongoing synchronization workflow rather than a one-time migration.

### 4. Improve review throughput and daily application use

M17 adds configurable suggestion groups, optional canonical automatic assignment, favorites, Unknown review state and web-triggered suggestion regeneration.

M18 reorganizes normal use around Review and Library, consolidates supported settings and removes routine startup dependence on command sequences.

These improvements operate on the same permanent catalogue and must not require a rebuild of canonical identities.

### 5. Add richer library intelligence

M19 adds EXIF/local capture metadata, location-aware smart collections and a bounded experiment for visible-content tagging. Model-generated tags remain derived evidence with provenance; manual tags remain canonical user data if added later.

## Azure remains optional

M09-M11 are retained because temporary Azure compute may still be useful for model experiments or large bounded batches. They are not version-1 gates and are not part of the trusted source or canonical catalogue path.

Use Azure only when it materially improves cost, throughput or experimental evidence. The local product must remain fully usable without it.

## Delivery rule

Do not create a second production catalogue for later phases. New ingestion, review automation, metadata and collection capabilities extend the same stable permanent catalogue with migrations and versioned derived data.