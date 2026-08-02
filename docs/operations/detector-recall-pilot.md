# Detector recall pilot

Use this procedure to measure face-detection recall without reviewing the complete archive. The sample contains 100 unique photos: 50 representative photos selected mechanically and 50 deliberately difficult photos.

Keep the completed tally outside Git because filenames, notes and counts may reveal private archive information. Only privacy-safe aggregate totals belong in milestone evidence.

## Decision target

The initial stop/go target is:

- at least 90% recall across all countable faces;
- at least 85% recall in photos containing five or more countable faces;
- no more than 10 false or duplicate detections across the 100 photos; and
- no obvious failure category that would materially harm the intended archive workflow.

These values are a product decision threshold for this pilot, not a general accuracy claim. Record the result before changing the target.

## Step 1: select 50 representative photos

1. Sort the 560-photo pilot by a stable rule, such as relative path or capture date.
2. Select every eleventh photo until 50 unique photos have been collected.
3. Do not replace an inconvenient photo unless it cannot be decoded. If one must be replaced, take the next photo in the same ordering and record that a replacement occurred.

This prevents selecting only photographs that already look easy or difficult.

## Step 2: select 50 difficult photos

Select 50 additional unique photos that are not in the representative set:

- 20 group photos containing five or more visible faces;
- 10 photos with small or distant faces;
- 10 photos dominated by profile, partially occluded, blurred or low-light faces; and
- 10 scanned, old or low-resolution photos.

A photo can have several difficulties, but assign it one primary category so the totals remain clear.

## Step 3: decide which faces count

Count a face when a person is visible and the face is recognisable at 100% zoom well enough that indexing the person would be useful.

Include:

- frontal faces;
- profiles;
- children and babies;
- partially occluded faces when enough facial structure remains visible; and
- blurred or low-light faces when a human can still recognise that a face is present.

Exclude from the primary metric:

- backs of heads;
- faces so tiny that they are only a few indistinguishable pixels at 100% zoom;
- statues, drawings, posters, television screens and reflections; and
- faces outside the intended photographic scene.

Record excluded screen, poster or reflection faces separately when they occur. Do not change the counting rule partway through the sample.

## Step 4: create the tally

Use a private spreadsheet or text file with one row per photo and these columns:

```text
sample_id
sample_group
primary_category
countable_faces
correct_detections
missed_faces
false_detections
duplicate_detections
notes
```

Use neutral sample IDs such as `R001` to `R050` and `D001` to `D050`. Keep private filenames in a separate local mapping only when needed.

## Step 5: review each photo

For each of the 100 photos:

1. Open the original photo or full preview at 100% zoom.
2. Count every face that meets the fixed counting rule.
3. Open the detector results for that same photo in the current review workflow.
4. Match each detected crop or box to one countable face.
5. Record one `correct_detection` for each countable face detected at least once.
6. Record one `missed_face` for each countable face with no matching detection.
7. Record a `false_detection` for a detector result that is not a countable human face.
8. When the same face is detected more than once, count one correct detection and record every additional result as a duplicate.
9. Add a short neutral note for the reason a face appears missed, such as `small`, `profile`, `occluded`, `blur`, `low_light` or `scan`.
10. Verify that `countable_faces = correct_detections + missed_faces` before moving to the next photo.

Do not identify people during this pass. The task is only to determine whether a face was found.

## Step 6: calculate the results

Calculate:

```text
overall_recall = total_correct_detections / total_countable_faces
group_recall = group_correct_detections / group_countable_faces
false_or_duplicate_total = total_false_detections + total_duplicate_detections
```

Also calculate recall separately for:

- representative photos;
- group photos;
- small or distant faces;
- profile, occluded, blurred or low-light faces; and
- scanned, old or low-resolution photos.

## Step 7: make the milestone decision

- When every decision target passes and no material failure category remains, complete WI-0034, cancel WI-0035 through WI-0038 as unnecessary, and complete M16.
- When recall fails but most misses have low confidence, continue to WI-0035 for a threshold sweep.
- When threshold tuning is insufficient and misses are mainly small faces, continue to WI-0036 for multi-scale YuNet detection.
- When multi-scale YuNet remains insufficient, continue to WI-0037 for another governed detector candidate.
- Any changed detector pipeline must complete WI-0038 before it is used with the canonical reviewed catalogue.

## Privacy-safe result template

Return only this aggregate summary to the repository:

```text
Sample: 100 photos; 50 representative and 50 difficult
Countable faces: N
Correct detections: N
Missed faces: N
Overall recall: N%
Five-or-more-face recall: N%
False detections: N
Duplicate detections: N
Representative recall: N%
Group recall: N%
Small/distant recall: N%
Profile/occluded/blur/low-light recall: N%
Scanned/old/low-resolution recall: N%
Decision target: pass/fail
Dominant privacy-safe miss categories:
Next action: stop / threshold sweep / multi-scale YuNet / alternate detector
```
