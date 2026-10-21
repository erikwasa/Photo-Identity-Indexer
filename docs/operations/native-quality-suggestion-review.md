## Accepted native Archive cleanup review — maintainer checkpoint (2026-10-21)

After merging PR #644, the maintainer verified that **Archive quality
review works as expected and existing feature regressions are OK**.
The accepted interactive behavior is full-viewport one-photo review,
keyboard 1 Keep / 2 Dismiss / 3 Consider, arrow previous/next, E opening
the separately confirmed exclusion dialog, C or Enter confirming only
within that dialog, and Escape cancelling. User manually chooses which
source copy to exclude; no unattended exclusion, OneDrive original change,
or collection/slideshow selection effect was introduced.

**WI-0629 / issue #629 is accepted and closed as Archive cleanup product
work.** The archive-side technical candidate queue and keyboard UX
are no longer blocked pending another 129-photo review. The next
distinct curation workstream is [issue #638](https://github.com/erikwasa/Photo-Identity-Indexer/issues/638)
for similar photos in collections/slideshows, and a separately calibrated
continuous quality-rating evaluation is [issue #639](https://github.com/erikwasa/Photo-Identity-Indexer/issues/639).
Do not reopen M37's consumed model held-out labels.

---

## v4: deliberate keyboard-confirmed exclusion (2026-10-21)

Maintainer confirmed the full-viewport single-photo reviewer now works much
better, and explicitly requested two further keyboard steps:

- **E** opens the in-place exclusion confirmation for the current photo.
- **C** or **Enter** confirms **Exclude from Photo Identity** *only while
  the confirmation dialog is displayed*. The dialog automatically focuses
  the Confirm button; **Enter** uses its normal native button activation,
  rather than executing a duplicate `keydown` exclusion request.
- **Escape** cancels. Keep/Dismiss/Consider keys 1/2/3 and ←/→ continue
  to work only when the exclusion dialog is closed.
- Key-repeat and Alt/Ctrl/Meta modified shortcuts are ignored for the
  application's keyboard handler. Enter does not initiate exclusion from
  the photo screen; no single key from the initial review screen can
  immediately purge a photo.
- Exclusion is the **existing irreversible source-copy purge** of
  Photo Identity's retained data (not deletion of the OneDrive original).
  This keyboard shortcut is a deliberate user action, not an automatic
  detector policy and not a reversible collection omission.

### Manual verification (important, destructive action)

After green CI and PR merge, update `main`, restart Photo Identity,
open `/quality-suggestions` and test:

1. On a photo you intend to **keep**, press `E`. Check the dialog opens
   *over the image*, and Confirm gets keyboard focus; then press
   `Escape`. Confirm the photo remains and the usual 1/2/3 and
   arrow keys work afterward.
2. Open the dialog again with `E`, then click **Cancel**. The
   dialog should close and keyboard review should resume.
3. The confirmation text and button should visibly advertise
   **C / Enter**. Confirm that pressing **C or Enter without an open
   dialog** does not exclude a photo. The static regression tests check
   that both paths are guarded behind the visible confirmation.
4. If you have a **genuinely unwanted photo that you independently
   intend to remove**, you may test `E` then `C` (or `Enter`) **once**.
   Verify the review queue advances and OneDrive original is untouched.
   **Do not perform a destructive confirmation on a photo kept solely
   for testing**. Live C/Enter purge need not be tested if no real
   exclusion is desired.

No new source hydration, automatic quality suppression, or collection
changes are introduced. Manual exclusion remains a two-action flow.

---

## v3: full-viewport keyboard review — maintained-app UX correction (2026-10-21)

The maintainer reported the native review remained unusable on a normal browser
viewport after the previous keyboard-focused PR: only about half of the photo
was initially visible because the hero/scan panel/filters appeared above the
image; navigation left the browser scrolled below the photo; selecting
"Exclude" inserted the destructive confirmation farther down the document;
and 1/2/3/arrow shortcuts did not function.

**Now the entire review is a fixed 100dvh viewport** separate from the
ordinary Photo Identity page layout: compact top toolbar, remaining available
height for one `object-fit:contain` image, and a permanently visible bottom
row of reasons + Keep/Dismiss/Consider/Previous/Next/Exclude buttons. Photo
dimensions cannot push actions below the fold. The scan controls open as an
overlapping drawer, not above the image. Explicit Exclude opens a centered
accessible confirmation dialog *over* the review screen. The existing
source-copy purge API is still invoked ONLY by clicking **Confirm exclude &
purge**, not by a number key or merely opening the dialog. The original
OneDrive photo is untouched.

**Keyboard contract**: native Blazor `@onkeydown` event handling on a focused
`tabindex=0` review screen, replacing the fragile JS-module document
listener. The review screen is auto-focused without scrolling at initialization
and again after filter changes / navigation. Press `1` for Keep, `2` for
Dismiss, `3` for Consider; `←` / `→` for previous / next. Pressing keys while
selecting filters is suppressed so form inputs work normally. There is
deliberately no destructive key binding; Escape cancels an open exclusion
confirmation. A modal is placed within the same viewport and cannot expand
the browser document.

### One short operator verification (after green CI/merge)

1. `git switch main; git pull --ff-only origin main`. Restart Photo Identity
   and open `http://127.0.0.1:5080/quality-suggestions`.
2. **Without scrolling**, verify the WHOLE photo is visible along with
   reasons, Keep/Dismiss/Consider, Previous/Next, and Exclude.
   Try both portrait and landscape examples if available; optionally zoom
   browser to 100%. The top area must remain compact.
3. Immediately press `1`, `2` or `3` and check that the choice saves and
   the next matching photo is displayed. Press `←`/`→`, then select a new
   reason/status filter and press numbers again **without clicking the image**.
4. Click Next/Previous: image and buttons remain visible without the browser
   jumping/scrolling to a retained old position.
5. Click **Exclude photo…**. Confirm its entire warning and **Cancel** /
   **Confirm exclude & purge** buttons are visible in a centered overlay,
   with no scrolling. Press Escape to cancel and confirm the photo remains.
   Do NOT perform an actual exclusion just to test this.
6. Open/close **Scan options** and confirm its drawer does not shrink/move
   the reviewed photo below the viewport. Existing next/all scan and
   restart-persisted decisions are unchanged.
7. Report PASS/FAIL for full-image visibility, keyboard actions, arrow
   navigation, in-place confirmation, and anything unexpected. Do not
   share private photo identifiers.

The new offline Python test `test_native_quality_viewport.py` guards the
source-level viewport and keyboard contract, while the real browser
verification above remains necessary before final UX acceptance.

---

## v2: one-photo keyboard review, direct confirmed exclusion, progressive scan (2026-10-21)

The maintainer's first native smoke test **passed all checks**: M37 docs,
suggestion images, Keep/Dismiss/Consider, independent context state,
persistence through restart, existing Archive/Search/Collections/Slideshow
behavior. There were approximately **30 native suggestions**. However,
the gallery required repeated scrolling and mouse clicks; the operator
preferred the prior fast loopback viewer, where 1/2/3 saved and arrows
moved to the adjacent photo. The original native refresh selected the
SAME deterministically hashed first 1,000 proxies each time, **not**
the next 1,000. This follow-up directly corrects those two limitations.

### Focused Archive-cleanup review

The Quality suggestions page now presents **one large proxy photo** and
a compact set of reasons with non-destructive keyboard decisions:

- `1` Keep; `2` Dismiss; `3` Consider exclusion. Saving a choice
  advances to the next photo when the filter is `Not reviewed`.
- Left/right arrow keys move to adjacent photos; keyboard shortcuts do
  not trigger when typing in forms or interacting with selects.
- Use Reason and Review status = Not reviewed / All /
  Consider exclusion to revisit any prior candidate; the review queue
  and operator choices persist across restarts and batch scans.
- **Exclude from Photo Identity…** enters an explicit on-screen
  confirmation. Only the distinct **Confirm exclude & purge** button
  invokes the exact existing native `POST /api/archive/exclusions/revision`
  source-copy boundary, and subsequently removes the now-ineligible
  candidate from the current suggestion queue. There is NO keyboard
  shortcut for this final destructive step, no bulk exclude, no silent
  exclusion by the low-quality detector and NO OneDrive original deletion.
  The operation is a durable source-copy-local purge, not a reversible
  hide. Use it only for genuinely unwanted photos, never for testing.

### Scan mode and progress — no full archive inference requirement

The native API now supports bounded `mode=next` and
`mode=restart` scans with `limit` 1–5000. The UI offers batch sizes
**100, 500, 1,000, 5,000** plus:

- **Scan next batch**: uses the retained next-offset cursor and
  scans the *next* set of current existing `jpeg-1600-q78` proxies
  in deterministic order. No repeats from clicking Refresh again.
- **Scan all remaining**: repeatedly invokes the SAME bounded
  endpoint to advance through the current eligible archive,
  retaining progress after each successful batch. **Stop after current
  batch** cooperatively stops launching subsequent requests and
  preserves completed work.
- **Start scan over**: deliberately begins at page zero (for a new
  archive sweep) and replaces the current suggestion candidate list;
  importantly, previous human Keep/Dismiss/Consider decisions remain
  saved and are restored where revision IDs appear again.

No original hydration, no external model weights, no automatic exclusion,
no background scheduling, no production collection/slide filtering, and
no unbounded single HTTP request. The local reviewer accepts new
candidate batches as they are prepared. In a changing catalogue,
offset pagination may overlap/skip a few items because a source revision
can be inserted/removed between scans: candidate IDs are deduplicated,
and every exposed review proxy is verified current/exclusion-aware.
If strict catalogue snapshot coverage is ever required, a separate
versioned-snapshot work item is needed. The persisted cursor is
automatically migrated from the previous native first-batch state:
an earlier 1,000-photo scan will resume at its recorded scanned count
instead of rerunning that first page.

### Simplified use cases and scope

Archive cleanup now focuses on **technically poor photos** and manual
Photo Identity-wide exclusion. The confusing combination of context
checkboxes plus dropdown has been removed from this page.
Existing persisted per-context decisions remain in local state,
but Collections/Slideshows are treated as **one distinct curation
workflow** rather than a second use of technical-only cleanup. The
follow-ons are:

- [Collection similarity groups and non-destructive optional omission](https://github.com/erikwasa/Photo-Identity-Indexer/issues/638)
- [Continuous, calibrated per-revision quality rating with provenance](https://github.com/erikwasa/Photo-Identity-Indexer/issues/639)

Neither future work item inherits M37's rejected automatic global
suppression as a production permission. A raw technical candidate
priority is NOT an absolute quality rating or a calibrated 0–100
score; no new model weights/inference/license are approved by the
current UX improvements.

### New local validation after merge

1. Update `main`, start the maintained Photo Identity launcher, open
   `http://127.0.0.1:5080/quality-suggestions`.
2. Check the old overlapping context checkboxes/dropdown are absent;
   only Archive quality cleanup is shown.
3. Check the scan counters and next-offset. **Scan next batch** with
   100 (or 500) photos, then scan another batch and verify cumulative
   scanned count advances, and old saved decisions remain.
4. Try `Not reviewed`, then press 1/2/3. Each saves and moves to the
   next photo without scrolling. Try left/right and reason filters.
5. Mark one candidate `Consider exclusion`; switch Review status to
   `Consider exclusion` to revisit it. Optionally open the
   **Exclude…** confirmation, read the purge text, then choose
   **Cancel**. Do **not** actually purge a photo just for testing.
6. Restart the app and verify the progress cursor and review
   decisions persisted. If desired, use **Scan all remaining** but
   it is not needed for acceptance; scans are read-only and
   deliberately bounded per request. Check Stop-after-batch.
7. Confirm Archive, Search, Smart/Creative Collections and slideshow
   still behave normally and no original was hydrated.

Report only aggregate counts, general usability and unexpected
behavior. No per-photo revision IDs, decisions or local proxy paths
need to be shared.

## Historical v1 behavior (superseded by the v2 steps above)

The following description documents the original native gallery before the keyboard and progressive-scan improvements. Its checkboxes, dropdown and repeatedly refreshed first batch no longer describe the current UX. Use the v2 procedure above for live operation.

# Native operator-controlled technical quality suggestions

WI-0629's **second substantive product phase** adds a built-in `/quality-suggestions`
page in Photo Identity. The first local companion review was fully used by
the maintainer: **129/129** Archive candidates reviewed, **72 Keep, 37 Dismiss,
20 Consider exclusion**; the maintainer judged **most considered photos
genuinely unwanted** and the review workflow **reasonably convenient**.
This supports proceeding with a native **manual** feature, not unattended
exclusion accuracy.

## New native workflow

1. Open Photo Identity, Advanced → Archive operations → **Quality suggestions**.
2. Select **Refresh suggestions** to examine a deterministic bounded sample
   of up to **1,000 current existing `jpeg-1600-q78` review proxies**.
   This is an *explicit* operator action, not an automatic scheduled scan,
   no original access, no hydration, no inference model weights. It uses
   the already implemented conservative OpenCV technical candidate flags.
   Each current derivative's size/SHA is checked before analysis; excluded
   source copies are filtered by the shared exclusion-aware proxy resolver.
   The archive scan doesn't mutate source data, the photo catalogue or
   existing collections, and produces at most 1,500 (default <=1,000)
   ranked candidates.
3. Browse suggested photos and their reasons (possible blur/focus, exposure,
   clipping, contrast, effective resolution), filter by reason and
   **All / Not reviewed / Consider exclusion**. Each photo has manual
   **Keep, Dismiss or Consider exclusion** choices.
4. Decisions are saved immediately under the operator's local app-data
   `PhotoIdentity/quality-suggestions/native-quality-suggestions-v1.json`.
   This state is independent from the PostgreSQL catalogue and cannot
   enact Exclude/Purge. It survives application restarts and a new bounded
   scan. When a candidate disappears from the new current population,
   it no longer appears in the suggestion queue, but prior human decision
   history is retained.
5. Context checkboxes **Archive cleanup (on), Collection review (off),
   Slideshow review (off)** control only where to review suggestions.
   Choices are independent per context. Enabling Collection or Slideshow
   does *not* change normal Search, Smart/Creative collection selection,
   Archive eligibility or slideshow playback.
6. Use **Consider exclusion** review-status filter to revisit all candidates
   you previously marked for possible exclusion. The native **Open photo**
   link leads to the existing `/photo/{revisionId}` viewer. Only the
   app's **separate, explicitly confirmed** Exclude from Photo Identity
   action can exclude and permanently purge that one source copy's local
   retained data; the OneDrive original is not deleted. No bulk automatic
   exclusion route exists.

## Bounded first verification, after CI and merge

Run normal application as before; no new migration, model installation
or private Python export is needed for the native page.

- Open **Advanced → Archive operations → Quality suggestions**.
- Before first scan, confirm empty-state wording and Archive context on
  while Collection and Slideshow contexts are off.
- Click **Refresh suggestions** once. Confirm counts and reasons appear,
  photos render from existing review-proxy endpoint, and the ordinary
  Archive, Search, Smart/Creative Collections and slideshow pages still
  work exactly as before.
- Mark a few as **Keep**, **Dismiss**, **Consider exclusion**. Switch review
  status to **Consider exclusion**; check it includes only the intended
  items and that the link opens native Photo viewer.
- Enable Collection review and give a different decision to one same photo.
  Switch back to Archive cleanup and confirm the earlier Archive decision
  is retained. Disable Collection review; nothing is removed from any
  saved collection.
- Reload the page, then restart the app and verify decisions persist.
  Click **Refresh suggestions** again and check decisions remain.
- **Do not actually exclude any photos just for verification**. The
  existing confirmed native purge action is intentionally separate and
  must be used only for a genuinely unwanted source copy.
- Report whether this new native UI is more convenient than the companion,
  the approximate number of suggestions, any missing/stale proxies, whether
  per-context controls make sense, and any unexpected original hydration
  or collection changes. Do not share revision IDs or source paths.

## Known limitations and policy boundaries

This is an **integrated app review experience**, not full-archive quality
scoring, not automated background ranking, not semantic near-duplicate
detection, not an absolute quality classifier, not a context-specific
reversible hide. The deterministic bounded sample is not representative
of the whole archive; the OpenCV low-sharpness flag often catches
intentional soft background or meaningful photos. An entry into
`Consider exclusion` expresses human intent and cannot be equated to
objective technical failure or a high-precision automated exclusion model.

The first native release creates **new local durable review decisions**,
separate from the earlier companion `private/wi0629-human-review-*.json`.
There is no automatic import of the companion's 129 private reviews;
those research files remain untouched. Native review data is not backed
up via the existing PostgreSQL backup; operators should include local
app-data JSON in any future preference backup/recovery plan.

Any future scoped reversible hiding/filtering needs an explicitly
accepted independent per-use-case design, not the existing durable
source-copy exclusion API. Future model use (e.g. TOPIQ) still requires
appropriate rights and a separate deliberate opt-in; not needed here.

## M37 relationship

The research evaluation M37 has concluded a **NO-GO for unattended**
Very Low exclusion and automatic similarity suppression. Its frozen
TOPIQ selective ranking is research-only, real collection A/B
effect deferred after **0/80** old score overlap and safe **80/80**
bounded planning. The **manual technical suggestion workflow** had a
successful 129/129 complete review and positive qualitative operator
feedback. Mark that specific *feasibility* as a **GO for native manual
review**, **not** a production quality/suppression model go/no-go.
The original WI-0195 four-arm experiment was not completed and must
remain historically identified as unmet/deferred.
