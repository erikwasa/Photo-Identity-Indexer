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
