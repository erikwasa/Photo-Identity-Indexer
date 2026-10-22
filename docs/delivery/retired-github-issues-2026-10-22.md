# GitHub issue retirement audit — 2026-10-22

## Decision and authority

**GitHub issues are no longer delivery/work-item authority for Photo Identity Indexer.**
Repository work-item documents, milestone membership and canonical
`docs/delivery/status/work-items/{active,archive}/WI-XXXX.yaml` shards are
the authority. Pull requests remain code-review/delivery evidence. No issues
should be opened, reopened or left open to carry separate project requirements.

This is a **full audit of all 33 surviving issues** returned by the GitHub
repository issues API (12 open and 21 closed, excluding pull requests) on
2026-10-22. Issue #638 had already been deleted after its migration to
[WI-0203](work-items/WI-0203-creative-collection-similarity-review.md) /
[WI-0204](work-items/WI-0204-reversible-collection-scoped-alternative-omission.md).
This file is historical **deletion-readiness inventory, not a live backlog**.

An issue is safe to **remove as a GitHub tracking artifact** only after its
content is represented by the cited repository work item, decision/runbook
or explicit supersession. This classification does **not** mean unimplemented
features are completed or authorize destructive archive operations.
Do not delete GitHub issues until the migration PR passes
`PhotoIdentity.Docs validate`, `generate --check` and is merged.

## Complete issue inventory and repository disposition

All numbers below are historical **GitHub issue numbers**, not WI IDs.
`Open`/`Closed` are the states observed in GitHub, not the
repository's lifecycle status.

| Issue | GitHub state | Canonical record / disposition | Deletion gate |
|---|---|---|---|
| #639 | Open | **New [WI-0205](work-items/WI-0205-continuous-photo-quality-rating.md), M40, proposed.** Continuous provenance-bearing quality rating; not shipped | Migrate full proposal and opt-in/legal/independent-validation gates |
| #629 | Closed | **New [WI-0206](work-items/WI-0206-native-quality-suggestion-review.md), M40, completed**, plus [native runbook](../operations/native-quality-suggestion-review.md) | Migrate 129-review and keyboard/source-safety evidence |
| #589 | Closed | [WI-0202](work-items/WI-0202-photo-catalogue-query-performance-regression.md), [PostgreSQL diagnostics](../operations/postgres-photo-query-regression-wi0202.md); resolved, recorded uncertainty | Replace obsolete issue evidence URL; do not conflate with still separate WI-0151 |
| #571 | Open | [WI-0196](work-items/WI-0196-tv-portrait-benchmark-framing.md), M38 ready | Preserve future work as repository status |
| #570 | Open | [WI-0201](work-items/WI-0201-tv-portrait-acceptance.md), M38 proposed | Preserve work and dependencies |
| #569 | Open | [WI-0200](work-items/WI-0200-portrait-context-detail-layout.md), M38 proposed | Preserve experiment and boundaries |
| #568 | Open | [WI-0199](work-items/WI-0199-portrait-context-to-detail.md), M38 proposed | Preserve experiment and boundaries |
| #567 | Open | [WI-0198](work-items/WI-0198-opt-in-tv-focus-playback.md), M38 proposed | Preserve opt-in, bounded render safety |
| #566 | Open | [WI-0197](work-items/WI-0197-subject-aware-portrait-zoom.md), M38 proposed | Preserve framing/face-safety fallback |
| #538 | Open | [WI-0195](work-items/WI-0195-quality-aware-collection-simulation.md), M37 **completed as research disposition**; four-arm product acceptance explicitly **not demonstrated** | Preserve no-go/defer and no production rollout, not an open issue |
| #537 | Open | [WI-0194](work-items/WI-0194-archive-wide-visual-redundancy-evaluation.md), M37 **completed as research/no-go**; extensive private human-pair and proxy evidence | Preserve research aggregate metrics and rejection of unattended suppression |
| #536 | Closed | [WI-0193](work-items/WI-0193-absolute-photo-quality-model-evaluation.md), M37 completed research, consumed one-time held-out | Preserve strict no-retune/no-production license caveat |
| #535 | Open | [WI-0192](work-items/WI-0192-representative-photo-quality-benchmark.md), M37 completed after maintainer verification | Preserve blinded sample design and private artifacts unchanged |
| #513 | Open | [WI-0187](work-items/WI-0187-search-creative-anchors.md), M36 **in_review** | Future work remains in YAML regardless of issue deletion |
| #512 | Open | [WI-0186](work-items/WI-0186-scoped-semantic-caption-search.md), M36 **in_review** | Future work remains in YAML regardless of issue deletion |
| #510 | Closed | **Accidental issue** (“Created in error — no action”); no legitimate requirement | No record beyond this audit needed |
| #503 | Closed | [WI-0185](work-items/WI-0185-automatic-assignment-audit.md), M35 completed; date-only filter, paging, correction and return-context verified | Accepted behavior and PRs retained |
| #499 | Closed | [WI-0165](work-items/WI-0165-slideshow-library-status-counts.md), M32 completed; post-preparation/reload Prepared receipt regression repaired | Preserve PR #505 and maintainer retest |
| #492 | Closed | [WI-0184](work-items/WI-0184-manual-collection-date-sort.md), M32 completed | Acceptance and implementation already in work item |
| #491 | Closed | [WI-0183](work-items/WI-0183-smart-collection-progressive-disclosure.md), M32 completed | Acceptance and implementation already in work item |
| #490 | Closed | [WI-0182](work-items/WI-0182-photo-quality-scoring-evaluation.md), M32 completed **no-adoption evaluation** | Keep 300-photo technical-rating no-go distinct from WI-0205 |
| #489 | Closed | [WI-0181](work-items/WI-0181-creative-collection-checkbox-alignment.md), M32 completed | Acceptance and implementation already in work item |
| #488 | Closed | [WI-0180](work-items/WI-0180-playback-preferences-ui.md), M32 completed | Acceptance and implementation already in work item |
| #487 | Closed | [WI-0179](work-items/WI-0179-smart-collection-orientation-filter.md), M32 completed | Acceptance and implementation already in work item |
| #485 | Closed | **Accidental placeholder issue**; no legitimate requirement | No work to migrate |
| #484 | Closed | [WI-0177](work-items/WI-0177-unified-slideshow-library.md), M32 completed; verified removal of intrusive revalidation prompt | Do not create duplicate WI |
| #481 | Closed | [WI-0178](work-items/WI-0178-slideshow-library-filtering.md), M32 completed; desktop/phone name filter and sorting accepted | Preserve UX verification |
| #480 | Closed | [WI-0177](work-items/WI-0177-unified-slideshow-library.md), M32 completed | Preserve unified library and verification |
| #476 | Closed | [WI-0175](work-items/WI-0175-slideshow-catalogue-recovery.md), M32 completed; local catalogue and refresh regression accepted | Preserve recovery and separate #499 Prepared bug |
| #474 | Closed | [ADR-0011](../decisions/ADR-0011-operator-authorized-caption-burst-bridge.md) and [Azure burst runbook](../operations/azure-caption-burst-bridge.md), accepted under WI-0128 capability | Proven local bridge/remote T4 procedure documented in repo, not new remote inference mode |
| #472 | Closed | **Superseded proposal** for direct remote Ollama mode (unmerged PR #473); replaced by #474's ADR-0011 local bridge | No authorization to implement the rejected remote mode |
| #461 | Closed | [WI-0176](work-items/WI-0176-full-app-landscape-navigation.md) plus its new historical settings/nav appendix; PRs #462/#463/#479/#483 | Preserve all merged UI/settings changes and acceptance, not just landscape shell |
| #439 | Closed | **Accidental placeholder** (“Created accidentally while loading connector schemas”) | No legitimate requirement |

**After this audit PR is merged and the pointers are cleaned up, all 33
remaining GitHub issues are deletion-eligible as *issues*.** That says
nothing about when WI-0186/0187/0196–0201/0205 are implemented; the
repository still contains their appropriate non-terminal statuses.

## High-value migrated issue details

### Issue #639 — proposed continuous quality score

This was the **only open feature requirement without a canonical repository work
item**. [WI-0205](work-items/WI-0205-continuous-photo-quality-rating.md)
preserves: the distinction between defect/aesthetic/emotional value,
monotone score direction, confidence, model/version and proxy provenance,
independent representative validation, domain biases, license/rights,
opt-in bounded/resumable scoring, deterministic missing-score fallback,
within-relevance comparison first, and no automatic exclusion. The consumed
WI-0193 held-out data remains unavailable for further model tuning.

### Issue #629 — delivered native Archive quality reviewer

Previously had a long issue-only chronology despite final acceptance.
[WI-0206](work-items/WI-0206-native-quality-suggestion-review.md) now
captures PRs #631/#633/#640/#643/#644, the **129 Keep/Dismiss/Consider**
decisions (72/37/20), native page/proxy-based suggestions, restart/context
isolation, viewport and keyboard fixes, double-action confirmation
and private source-copy/OneDrive boundary. Human review success is **not**
license to automatically purge or to deploy a quality model.

### Issue #461 — early phone/navigation/settings consolidation

PR #462 fixed mobile nav overflow; PR #463 removed unused Library/Progress
entries, consolidated suggestion policy into Settings, relocated manual
match-regeneration and lifecycle tools into Advanced, enabled restart-applied
archive-storage settings in **GB** and compacted settings into accessible
sections. PR #479 addressed full-app landscape overflow; PR #483 fixed the
coarse-pointer landscape header/badge strip. Maintainer accepted WI-0176
on 2026-10-02. The details are additionally retained as an appendix to
[WI-0176](work-items/WI-0176-full-app-landscape-navigation.md);
no separate live GitHub issue is needed.

### Issues #472 / #474 — superseded remote-compute implementation

The proposed direct Remote Ollama mode (#472 / unmerged PR #473)
was **not** accepted. The proven method used unchanged loopback-only Photo
Identity plus a local bridge forwarding bounded 480x320 caption thumbnails
to operator-owned Azure HTTPS Ollama, governed by ADR-0011. The operator
achieved **12/12 success**, mean **122.18s local vs 3.92s Azure** and
**31.15x** observed acceleration, completed the caption backlog and
scaled idle GPU to zero. Migrate only the accepted runbook/decision and
explicitly reject unintended direct remote mode; see [Azure runbook](../operations/azure-caption-burst-bridge.md).

### M37 research issues #535–#538

The issues' research/checkpoint comments are not a remaining product backlog.
The extensive canonical WI-0192/0193/0194/0195 reports and
[M37 final decision ledger](milestones/M37-decision-ledger.md) preserve
the stratified/blinded benchmark, one-time held-out research authorization
and constraints, 27,050-proxy redundancy evidence, 200+180 reviewed
pairs, **NO-GO** unattended exclusion/suppression, and incomplete original
four-arm simulation. Never reopen the consumed held-out labels to justify
future production quality or visual suppression.

## Repository reference cleanup

Canonical active/archived YAML shards should not contain a link whose only
purpose is to point at a deleted issue. The administrative replacement uses
existing PR evidence or repository documentation while keeping **all work-item
lifecycle statuses and milestone dependencies unchanged**. The generated
`work-items.yaml` and `work-items-index.md` are regenerated from
canonical shards. Historical narrative issue numbers may remain as plain
provenance labels but not as active planning links. **Do not change status to
completed merely because an issue is deleted.**

## Deletion checklist and verification

1. Merge the audit/migration PR **only after CI passes** (including
   `PhotoIdentity.Docs validate` and `generate --check`).
2. Verify [WI-0205](work-items/WI-0205-continuous-photo-quality-rating.md)
   stays **proposed**, [WI-0206](work-items/WI-0206-native-quality-suggestion-review.md)
   stays **completed**, and the M38/M36 pending items retain their old status.
3. Review the inventory above and then delete the 33 issues in GitHub's
   issue UI (connected GitHub tooling does not expose permanent deletion).
   There is **no need to wait for proposed features to be implemented**:
   deleting the GitHub issue is a tracking migration, not project acceptance.
4. Re-query the GitHub issues API with `state=all` **excluding pull requests**
   until it returns **zero**, including closed issues. The issue count displayed
   by GitHub should also be zero. Confirm no new issues were created during
   the cleanup.
5. Continue normal work through `PhotoIdentity.Docs show/next/start/review`
   and milestone/work-item documents, never through issues.

**Do not delete GitHub pull requests**, which are legitimate development
review/evidence artifacts and are not counted as issues in this inventory.
