# M19 consolidated maintainer verification

This checklist records the M19 extension verification. The original WI-0050/WI-0056 baseline was already verified; the 2026-08-19 pass completed six later extension items and exposed the remaining archive-to-metadata/GeoNames integration gap now owned by WI-0072.

Run pending verification against a current build from `main`. Use normal catalogue data where safe. Any irreversible person-merge check should use a disposable test pair or known duplicates only; automated coverage remains the primary evidence for merge semantics if no safe local pair exists.

## Maintainer result — 2026-08-19

The maintainer completed the consolidated pass and reported **PASS** for WI-0061, WI-0062, WI-0063, WI-0066, WI-0067 and WI-0068. Those six items are verified for completion.

WI-0064 and WI-0065 remain in review. During a new 190-image archive run, the images downloaded, were source-verified and analyzed, but capture date/GPS did not appear. Repository inspection confirmed that the WI-0050 metadata reader was invoked only by explicit bounded photo-metadata backfill, not by archive advancement. Newly analyzed revisions therefore did not automatically become eligible for GPS-driven GeoNames enrichment. WI-0072 implements that missing lifecycle step and expands Photo Details metadata.

The same run displayed `Waiting for OneDrive to finish a managed download or release.` while useful work continued. That status is recorded as a separate operator-clarity follow-up rather than evidence that the 190-image run stopped.

Additional UI/navigation findings and proposed solution groupings are recorded in [M19-maintainer-review-2026-08-19.md](M19-maintainer-review-2026-08-19.md). M19 remains `in_progress` until WI-0072 and the WI-0064/WI-0065 live automatic-pickup checks complete.

## Suggested test data

Prepare or identify:

- one photo whose original is already local and has at least one confirmed person;
- one online-only photo with a durable review proxy if available;
- one photo containing a known person whose face was not detected, for manual photo-level presence;
- one photo/revision with GPS that is eligible for GeoNames enrichment;
- one person that can safely be hidden/unhidden from Smart Collections;
- one saved Smart Collection containing that person before hiding them;
- one person with at least two assigned faces for featured-photo testing;
- for WI-0072, at least one known date/GPS-bearing JPEG and one representative iPhone HEIC/HEIF file whose capture metadata can be independently checked.

## 1. Photo Details and navigation — WI-0061

1. Open a photo from a saved Smart Collection on a non-first result page if practical.
2. Confirm Photo Details shows the original file **name only**, confirmed people, and no private source path.
3. Use browser/mouse Back and confirm the same saved collection and result page are restored.
4. Create or use an unsaved Smart Collection preview, open a result, then use browser/mouse Back and confirm the preview filters/results are restored in the same browser tab.
5. Confirm the Photo Details Back control is context-aware when opened from Smart Collections and falls back safely when no valid return context exists.
6. For an already-local revision-verified original, confirm Photo Details displays the original rather than the proxy.
7. For an online-only original, confirm normal viewing can use the proxy without hydrating the original. If explicitly using `Load original`, confirm the same Photo Details view switches to the original when ready.

Pass condition: navigation context survives both saved and transient flows, metadata stays privacy-safe, and ordinary viewing does not hydrate online-only originals.

**Result 2026-08-19: PASS.**

## 2. Manual photo-level people — WI-0062

1. On a photo where a known person has no detected face, add that canonical person from Photo Details.
2. Confirm the person appears in Photo Details as manual presence without creating a face occurrence/review item.
3. Create/preview a Smart Collection filtered by that person and confirm the photo matches.
4. Confirm Face Review/face evidence behavior for the photo/person has not changed.
5. Remove the manual presence and confirm the Smart Collection no longer matches solely because of that manual assignment.

Pass condition: manual presence affects Photo Details and Smart Collections only as photo-level metadata and never masquerades as face evidence.

**Result 2026-08-19: PASS.**

## 3. First-class Places — WI-0063

1. In Photo Details, set a place and then replace it with a more-specific descendant.
2. Confirm only one effective place is shown and the normal UI omits the literal `Places/` prefix.
3. Confirm Places entries are absent from ordinary Tags selection.
4. In Smart Collections Location, select an ancestor such as `Sweden` and confirm photos assigned below that hierarchy match.
5. Select a more-specific node and confirm matching is based on the canonical hierarchy rather than unrelated leaf names.
6. If useful, combine named place with people/tags/taken-time criteria and confirm the dimensions retain AND semantics.

Pass condition: Places behave as one hierarchical Location value, not as generic tags.

**Result 2026-08-19: PASS.**

## 4. GeoNames provider result and automatic orchestration — WI-0064 / WI-0065

The configured live GeoNames account, normalization, long-place compatibility and Smart Collection location integration were already exercised during WI-0064. The remaining pass focuses on unattended orchestration after WI-0072 supplies metadata automatically.

1. With GeoNames configured and automatic enrichment enabled, add/process a GPS-bearing revision that has not already completed the current provider contract.
2. Do **not** press metadata Backfill or the maintenance Enrich button.
3. Confirm Photo Details reports the revision as metadata-inspected and displays its GPS coordinates.
4. Confirm the eligible photo eventually receives the expected automatic Place while the browser is free to navigate elsewhere.
5. Confirm archive/local processing itself is not held open waiting for the GeoNames result.
6. For restart/resume, leave at least one eligible/retryable revision outstanding, close Photo Identity, restart it, and confirm the worker resumes from durable SQLite state without re-entering a browser batch.
7. Confirm an existing manual Place or explicit manual clear is not silently overwritten by automatic enrichment.

Pass condition: normal metadata-to-GeoNames enrichment is server-side, provider-paced, restart-resumable and independent of a long-lived browser request; manual precedence remains intact.

**Result 2026-08-19: INCOMPLETE.** Prior live-provider behavior remains valid. Repeat this section after WI-0072 is merged using newly processed metadata-bearing revisions.

## 5. Smart Collection person visibility — WI-0066

1. In Maintain People, hide a test person from Smart Collections and reload the application/page; confirm the preference persists.
2. Confirm the hidden person remains visible/usable in Maintain People and normal face review/details.
3. Open a new Smart Collection and confirm the hidden person is absent from normal people discovery/search.
4. Reopen a saved Smart Collection that already referenced the person before they were hidden. Confirm the person remains selected, is explicitly marked `Hidden`, and the collection reevaluates with the same person criterion/results.
5. Remove that hidden selection and confirm it cannot be reselected while hidden.
6. Unhide the person and confirm they return to normal discovery.
7. Confirm unrelated tag/location/date Smart Collections containing photos of that person are not suppressed by the visibility preference.

Pass condition: hiding is strictly a reversible Smart Collection discovery preference and never weakens identity evidence or unrelated collection results.

**Result 2026-08-19: PASS.** Follow-up UX polish is still desired: make hidden status more visually obvious and place hidden people after visible people on Maintain People.

## 6. Featured representative faces — WI-0067

1. Open Face Details for an assigned named face and set it as that person's featured photo.
2. Confirm the explicit state is shown and Maintain People displays the same representative portrait.
3. Confirm the main Face Details review image remains the face occurrence being reviewed rather than being replaced by some other representative occurrence.
4. Clear the explicit choice back to automatic and confirm the resolved representative state updates deterministically.
5. If a safe disposable/known-duplicate person pair exists, optionally verify merge presentation behavior: an existing survivor preference wins; otherwise a valid source preference may carry to the survivor. Do not perform an irreversible merge solely for this checklist on valuable catalogue identities.

Pass condition: representative portraits are presentation metadata, explicit/automatic state behaves predictably, and identity assignments/evidence are unchanged.

**Result 2026-08-19: PASS.** Maintain People card containment is recorded separately as responsive layout polish rather than a failure of representative-face semantics.

## 7. Searchable portrait-led Smart Collection people picker — WI-0068

1. Type part of a display name using different casing and confirm candidates filter incrementally and case-insensitively.
2. Select a person, then change the search text so their name no longer matches; confirm the selected person remains visible and removable in the selected area.
3. Clear the search and confirm normal visible candidates return.
4. Confirm candidates show their resolved representative portraits; verify a person without a representative face remains selectable with a stable neutral fallback.
5. Reopen the saved collection containing the hidden person from the WI-0066 check and confirm the hidden selected person remains in the selected area but is not discoverable as a new candidate.
6. Remove the hidden selected person and confirm search cannot rediscover them until they are unhidden.
7. Exercise both `All selected` and `Any selected` on known people and confirm the existing matching semantics are unchanged.
8. Rename a selected person in Maintain People, reopen the saved collection, and confirm the definition still refers to the same canonical person and displays the new name.
9. Use keyboard navigation/tab focus for search, candidate add and selected-person remove actions; confirm the display name remains the accessible identity label.

Pass condition: search affects discovery only, selections remain stable canonical PersonIds, hidden-person compatibility is preserved, portraits are supplementary cues, and `all`/`any` semantics remain unchanged.

**Result 2026-08-19: PASS.** Smart Collection image/card containment is recorded as a separate visual follow-up.

## 8. Archive metadata ingestion and Photo Details — WI-0072

Run this after the WI-0072 implementation is merged.

1. Add or synchronize a new archive folder containing at least one JPEG and one representative iPhone HEIC/HEIF file with known capture date/time; at least one should contain known GPS coordinates.
2. Start normal archive advancement. Do **not** invoke `/api/photo-metadata/backfill` or any manual metadata operation.
3. Confirm the photos become verified/analyzed normally and archive advancement does not add a second metadata-only hydration cycle.
4. Open each photo and confirm **Capture metadata** reports `Inspected`.
5. Confirm **Photo taken** matches the photographic capture timestamp. If a real source offset is available, confirm it is shown separately; if no offset exists, confirm no UTC conversion is invented.
6. Confirm **Camera make**, **Camera model** and other available lens/exposure fields match the source metadata.
7. On the GPS-bearing file, confirm exact latitude/longitude are shown and reasonable; confirm GPS altitude when present.
8. Expand **All metadata** and confirm useful EXIF/XMP tags are present while embedded thumbnail/preview/binary payloads are absent.
9. Identify a file with no supported capture metadata if practical and confirm it shows `Inspected` rather than `Not inspected`, with an explicit no-key-fields message.
10. For an online-only revision that archive processing does not otherwise need to hydrate, confirm metadata inspection alone does not make it local.
11. Continue into the WI-0064/WI-0065 steps above and confirm the newly persisted GPS enters automatic GeoNames enrichment without a manual Backfill/Enrich action.

Pass condition: metadata is captured automatically from the exact local/hash-verified revision, no independent hydration path is introduced, JPEG/HEIC real-media fields display correctly, raw metadata remains bounded/safe, and persisted GPS flows into the existing asynchronous GeoNames worker.

**Result: PENDING.** Automated implementation coverage exists in PR #191; real-media and live-provider verification is required after merge.

## Completion recording

The 2026-08-19 pass verifies WI-0061, WI-0062, WI-0063, WI-0066, WI-0067 and WI-0068 for completion. WI-0064, WI-0065 and WI-0072 remain open until section 8 plus the automatic GeoNames pickup/restart-resume checks pass on newly processed metadata-bearing revisions.

M19 remains `in_progress` until all active M19 work items are complete. Other defects and requested enhancements found during the pass remain tracked in [M19-maintainer-review-2026-08-19.md](M19-maintainer-review-2026-08-19.md) and should become focused follow-up work items rather than weakening already-passed acceptance criteria.
