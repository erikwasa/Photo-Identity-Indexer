# WI-0033 manual verification on Windows and Pixel

Use this procedure only after the final WI-0033 preparation pull request is merged and its CI run is green. Automated smoke protects published routes, API invariants, audit linkage and privacy boundaries; it does not prove touch comfort, mobile keyboard behaviour or real review throughput.

## Required result

Complete one Windows session and one Pixel session. Each session must review between 50 and 100 initially unreviewed faces and record:

- active review minutes;
- faces reviewed;
- faces per minute;
- suggestions accepted, including faces accepted through a confirmed bulk group;
- explicit decision actions;
- explicit actions per accepted suggestion;
- forced returns to the gallery; and
- decisions undone within ten seconds because the first decision was wrong or accidental.

A device session fails if any mandatory interaction, audit or privacy check fails. Record the failed check names with `-FailedChecks` rather than describing private photos or people.

## Safety and preparation

1. Use a trusted private network. The review listener is unauthenticated HTTP and must not be exposed to a guest, public or untrusted network.
2. On Windows, confirm the active network profile is **Private**. Permit the chosen TCP port only on that profile if the firewall blocks Pixel access.
3. Pull the merged branch and confirm the working tree is clean.
4. Back up the real catalogue before the throughput sessions.
5. Prepare a private queue containing 50–100 initially unreviewed faces with ranked suggestions for one exact model revision.
6. For a like-for-like comparison, take a catalogue snapshot before the Windows run and restore that same snapshot before the Pixel run. Do not run both sessions against one continuously mutating queue.
7. Keep photos, person names, database files, face identifiers, paths and biometric data out of notes and repository commits.

## Automated gate

From PowerShell at the repository root:

```powershell
./verify-review.ps1 -Mode Smoke -Configuration Release
```

Pass criteria:

- the command exits successfully;
- `.artifacts/review-verification/verification-report.json` has `result` equal to `passed`;
- every entry below `smoke` is `passed`;
- no private catalogue is changed because the command uses a disposable synthetic catalogue.

Do not continue if this gate fails.

## Synthetic interaction check

Start the disposable published application:

```powershell
./verify-review.ps1 -Mode Interactive -Configuration Release -Port 5080
```

Use the printed localhost URL on Windows and the printed LAN URL on Pixel. Keep the PowerShell window open until both synthetic checks are complete.

On each device:

1. Open **Faces**. Confirm there is no horizontal page scrolling.
2. Create a temporary person. On Windows, type the name and press **Enter**. On Pixel, use the keyboard action button. Confirm exactly one person is created.
3. Open an unreviewed face from a filtered queue. Confirm queue position plus Previous and Next are visible and usable.
4. Accept a suggestion. Confirm the application advances to the next eligible face without returning to the gallery.
5. Use Previous or Next after another decision. Confirm the preserved state, model revision and sort order still describe the same queue.
6. Open **Suggestions**. Confirm the suggested person, score, margin and exact model revision are readable without opening every face.
7. Open **Bulk suggestions**. Select one suggested-person group, preview it and confirm the affected count is obvious before commit. Cancel or use the disposable data only.
8. Open **Audit**. Select a person and confirm every active assignment is visible, details links work and disagreement indicators are advisory.
9. Open face details and the Audit response. Confirm no local filesystem path, crop storage path or embedding is displayed.
10. Confirm buttons, selectors and navigation controls are comfortable to operate without accidental activation. On Pixel, perform this check one-handed and in portrait orientation.

Stop the disposable server by returning to PowerShell and pressing Enter.

## Real Windows throughput session

1. Restore the prepared pre-session catalogue snapshot.
2. Start the normal local review host against that catalogue on the trusted Windows machine. Use the same exact model revision planned for Pixel.
3. Open **Suggestions**, choose **Needs review**, select the exact model revision and use **Easiest suggestions** or **Suggested person** ordering.
4. Start a stopwatch only when active review begins. Pause it for interruptions, troubleshooting, phone calls or time spent away from the task.
5. Review 50–100 faces. Use the normal continuous details flow for ambiguous items and **Bulk suggestions** for clear same-person groups.
6. Count metrics using the definitions below.
7. During the session, deliberately exercise all mandatory checks:
   - create one temporary person with Enter and confirm one creation;
   - accept a suggestion and confirm automatic advance;
   - use Previous and Next;
   - assign one face manually;
   - reject one false detection;
   - immediately undo one disposable test decision, then make the correct decision;
   - preview and confirm one bulk suggestion group;
   - audit one person and inspect at least one disagreement or the zero-disagreement state;
   - restart the host after a safe checkpoint and confirm committed decisions persist.
8. Stop the stopwatch when the target face count is reached.
9. Record the session with `record-review-session.ps1`.

Example:

```powershell
./record-review-session.ps1 `
  -Device Windows `
  -FacesReviewed 75 `
  -ActiveMinutes 18.4 `
  -AcceptedSuggestions 62 `
  -ExplicitActions 78 `
  -GalleryReturns 0 `
  -ImmediateUndos 1 `
  -Notes "Completed without private identifiers."
```

## Real Pixel throughput session

1. Stop the real host and restore the same prepared pre-session catalogue snapshot used before Windows.
2. Start the normal review host listening on the private LAN address. Confirm the firewall rule is limited to the Private profile and chosen port.
3. On Pixel, open the LAN URL in the normal browser in portrait orientation. Do not use a public tunnel or relay.
4. Select the same state, exact model revision and ordering used on Windows.
5. Repeat the Windows throughput procedure for 50–100 faces. Use the mobile keyboard action when creating the temporary person.
6. Pay particular attention to one-handed reach, sticky actions, selector usability, accidental taps, text clipping and horizontal scrolling.
7. Stop the active-time stopwatch at the target count and record the session.

Example:

```powershell
./record-review-session.ps1 `
  -Device Pixel `
  -FacesReviewed 75 `
  -ActiveMinutes 21.1 `
  -AcceptedSuggestions 60 `
  -ExplicitActions 79 `
  -GalleryReturns 0 `
  -ImmediateUndos 1 `
  -Notes "Portrait, one-handed interaction passed."
```

When both device files exist, the script creates:

```text
.artifacts/review-verification/manual/manual-verification-summary.json
```

Keep these reports local. Share only privacy-safe aggregate values when updating delivery evidence.

## Metric definitions

**Faces reviewed** counts faces that leave the initially unreviewed queue through an accepted suggestion, manual assignment or face rejection. A bulk commit contributes its affected face count.

**Accepted suggestions** counts faces accepted from matcher suggestions. A bulk suggestion commit contributes its affected face count, not one.

**Explicit actions** counts intentional decision-work actions: single Accept, Assign or Reject; bulk Preview; bulk Commit; and Undo. Do not count scrolling, opening details, Previous, Next or changing a harmless filter.

**Gallery returns** counts occasions where the operator had to return to a gallery merely to continue the same queue. Intentional visits to Audit or Bulk suggestions do not count.

**Immediate undos** counts decisions undone within ten seconds because the original decision was accidental or wrong. The deliberately exercised undo should be counted and explained only as a synthetic verification action, without private details.

## Mandatory pass checklist

Record a failed check name if any item below fails:

- `person-create-enter` or `person-create-mobile-keyboard`;
- `single-create-no-duplicate`;
- `queue-scope-preserved`;
- `previous-next-usable`;
- `accept-auto-advance`;
- `no-face-skip`;
- `suggestion-summary-readable`;
- `exact-model-visible`;
- `bulk-group-same-person`;
- `bulk-preview-count-clear`;
- `bulk-linked-audit`;
- `person-audit-complete`;
- `disagreement-advisory`;
- `no-horizontal-scroll`;
- `touch-targets-comfortable`;
- `no-private-paths`;
- `decisions-persist-after-restart`.

Example failed report:

```powershell
./record-review-session.ps1 `
  -Device Pixel `
  -FacesReviewed 50 `
  -ActiveMinutes 24.0 `
  -AcceptedSuggestions 38 `
  -ExplicitActions 61 `
  -GalleryReturns 2 `
  -ImmediateUndos 3 `
  -FailedChecks "previous-next-usable", "touch-targets-comfortable" `
  -Notes "Failure recorded without private content."
```

A failed session is useful evidence. Do not mark WI-0033 complete until both device reports pass or the failed checks are fixed and rerun.

## Completion evidence

After both sessions pass, update WI-0033 with only:

- Windows and Pixel faces reviewed;
- active minutes and faces per minute;
- accepted suggestions and explicit actions per accepted suggestion;
- gallery returns and immediate undos;
- confirmation that all mandatory checks passed;
- the merge commit and green CI run for the final preparation PR.

Do not commit the generated JSON reports, real catalogue, screenshots containing names, photos, local paths or other private data.
