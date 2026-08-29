# Source-copy lifecycle and privacy exclusion

## Purpose

Photo Identity must handle source copies independently while remaining conservative about source deletion, moves and privacy-sensitive exclusion.

The source archive is the operator's authority. Photo Identity may detect exact duplicate content and may reconcile an ordinary included photo across an unambiguous move, but it must not silently merge independent source copies or propagate an exclusion to another path.

## Source-copy identity

V1 exclusion is scoped to one source locator: the stable source plus its normalized source key/path.

Two paths containing byte-identical content remain two independently controllable source copies. Exact SHA-256 equality is useful for duplicate inventory and move reconciliation, but it does not make the copies one asset for operator actions.

An excluded source locator remains excluded while that same locator is present or later reappears. Re-inclusion is an explicit operator action.

## Exact duplicates

When authoritative content hashes are available, Photo Identity may group source copies with the same SHA-256 as exact duplicates.

Exact duplicate grouping:

- is informational and operator-facing;
- does not merge assets, revisions, review history or source ownership;
- does not automatically suppress a copy from normal workflows;
- does not propagate exclusion from one source copy to another;
- must not infer equality from filename, size or timestamps alone.

Near-duplicate, resized, recompressed, edited or cropped-photo detection is outside this contract. Those require a separate perceptual-similarity policy because false positives are possible.

## Source deletion

When a previously catalogued source path is no longer observed, Photo Identity marks it removed/missing without immediately erasing its local catalogue or review data.

Immediate purge is intentionally avoided because a move, rename, temporary OneDrive state or partial/scoped scan can initially look like deletion.

After move reconciliation has had a chance to run, genuinely missing source copies remain available in an operator-facing **Removed from source** review queue. The operator can select one or more entries and choose **Exclude & purge**.

Until that explicit action, retained review proxies may be used only to support the removed-source review workflow.

## Move and rename reconciliation

Ordinary included photos may preserve their existing AssetId across a source move or rename when reconciliation is exact and unambiguous.

Automatic move reconciliation requires authoritative content identity and conservative source-state evidence:

- one old included source copy is missing;
- one newly discovered source copy has the same authoritative SHA-256;
- the old path is no longer present;
- no competing missing/current copies make the match ambiguous.

If the old path still exists, the new path is a duplicate copy rather than a move.

If multiple candidates share the same hash, Photo Identity must not guess. Ambiguous cases stay as separate source copies until a later explicit resolution capability exists.

An online-only or otherwise unverified new source item must not be treated as an exact move solely from metadata.

### Excluded copies do not follow moves

Excluded source locators never participate in automatic move reconciliation.

If an excluded file is renamed or moved, the new path is a new source copy from Photo Identity's perspective and is not automatically excluded. The operator must exclude that new copy separately if desired.

This rule is deliberate even when the new copy has the same SHA-256 as the previously excluded copy.

## Manual exclusion

**Exclude from Photo Identity** is a privacy boundary for one source copy.

Exclusion must:

1. become durable before cleanup begins;
2. immediately block normal application access to that source copy;
3. prevent new analysis, metadata extraction, proxy generation, face processing, identity matching, Smart Collection membership and slideshow access;
4. prevent original-image/hydration/viewer endpoints from exposing the source through Photo Identity;
5. purge Photo Identity's locally retained photo-specific data and derivatives;
6. leave the original source file untouched.

Photo Identity must never delete the OneDrive/source original as part of exclusion.

## Purge contract

Once exclusion starts, Photo Identity completes a crash-safe, resumable purge.

The purge removes photo-specific retained data including, where present:

- review proxies and proxy records;
- face review derivative files;
- face crop files and detector/reconciliation inspection crops;
- face occurrences and detector observations;
- embeddings and identity suggestions;
- face/person assignments and review history that reference the excluded photo;
- manual photo/person associations;
- photo tags and Places/location actions;
- EXIF/capture metadata and other revision metadata;
- analysis/proxy completion state;
- immutable asset revisions and their content hashes.

Shared person records are not deleted merely because one excluded photo referenced that person. The relationship between the excluded photo and the person is deleted.

The implementation must delete filesystem derivatives before discarding the durable information needed to locate them. Failed file deletion must remain retryable and visible as **Purge pending** or **Purge failed** while media access remains blocked.

After purge, Photo Identity retains only the minimum source-locator exclusion tombstone and operational audit/status required to keep that locator excluded and to finish/retry cleanup. The tombstone must not retain photo pixels, content hashes, dimensions, location, tags, face information or identity links.

This privacy purge is an intentional exception to normal append-only review/audit retention.

## Restore

A purged excluded source locator can later be explicitly included again.

Restore does not resurrect deleted Photo Identity data. If the source is still available, it is catalogued and analyzed again from scratch under current processing policy.

## Archive/operator surfaces

Archive should make lifecycle states directly reviewable:

- **Removed from source** - source copy is missing and awaits operator decision after reconciliation;
- **Exact duplicates** - independently selectable source copies that share an authoritative SHA-256;
- **Excluded** - source locator is blocked and purge completed;
- **Purge pending** - blocked, with cleanup still in progress;
- **Purge failed** - blocked, with cleanup requiring retry/operator attention.

Duplicate views must make each source copy independently selectable. There is no implicit **exclude all copies** action.

Manual exclusion should also be available from the normal photo-detail/archive workflow for a still-present source copy.

After purge completes, an excluded entry must not offer a thumbnail, original viewer or any other photo-content preview.

## Security invariant

A durable exclusion check must exist below presentation-layer filtering. A missed UI query filter must not make an excluded source copy viewable or processable.

At minimum, schedulers, metadata enrichment, recognition, Smart Collections, slideshows, proxy/original media resolution and hydration must refuse excluded source locators.

## Non-goals

- Deleting source originals from OneDrive.
- Automatically excluding exact duplicate copies.
- Carrying an exclusion across a move or rename.
- Automatically merging duplicate assets or review history.
- Perceptual/near-duplicate detection for crops, edits or recompressed images.
- Automatically purging a source copy solely because it disappeared.
