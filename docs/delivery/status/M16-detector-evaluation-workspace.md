# M16 detector evaluation workspace status

Status date: 2026-08-07

Active implementation branch: `agent/WI-0038-detector-rollout`

## Current outcome

The fixed private 100-photo M16 set and frozen face-level ground truth have now been used to complete the governed detector search.

The reviewed YuNet results were:

- confidence `0.9` baseline: failed the complete gate;
- single-pass confidence `0.8`, `0.7`, `0.6` and `0.5`: all failed the complete gate;
- multi-scale confidence `0.9`: improved on relevant earlier YuNet runs but still failed; and
- multi-scale confidence `0.7`: produced more than 100 false or duplicate detections, so lower multi-scale confidence was not pursued.

No YuNet configuration is approved for rollout.

WI-0037 qualified the exact pinned CenterFace ONNX artifact, corrected its local runtime path without changing the governed candidate settings and completed the first alternative-detector comparison.

On 2026-08-07 the maintainer explicitly accepted the documented CenterFace pretrained-weight/training-data uncertainty for local evaluation. The maintainer then completed the unchanged CenterFace confidence `0.5`, `single-pass` comparison, reported that it **passed the complete M16 gate**, and instructed WI-0038 rollout engineering to continue. Detailed counts and category evidence remain private.

PR #92 added a `Neutral` comparison outcome for legitimate face detections that were intentionally outside the frozen countable-face scope. Neutral resolves review workload but does not increase recall and is not included in the false-plus-duplicate penalty.

WI-0037 is complete. WI-0038 is active and is now the only remaining M16 engineering work before detector rollout can be considered safe for the canonical local catalogue.

## Selected detector pipeline

The selected local pipeline is:

- detector `centerface-2019-fp32`;
- SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`;
- OpenCV DNN with a fresh native `Net` per image;
- confidence `0.5`;
- `single-pass`;
- RGB float32 scale `1.0`, zero mean;
- maximum source long edge `1600` before multiple-of-32 rounding;
- CenterFace NMS `0.30` and top-K `5000`;
- SFace `sface-2021dec-fp32`;
- `sface-five-point-v1` alignment; and
- padding `0.25` in the local inspection workflow.

The local-evaluation governance acceptance does not establish redistribution rights for the pretrained model. The provisional weight/training-data boundary remains recorded in the CenterFace qualification document.

## Delivered evaluation workspace

### Baseline authoring and export — PRs #70, #71 and #72

- photo-level processing-run queries including zero-detection photos;
- original-photo streaming without source paths;
- private manifest import and immutable-run validation;
- resumable private JSON sessions outside the catalogue;
- detector classification and direct missed-face authoring;
- source-pixel zoom/pan; and
- spreadsheet-compatible export.

### Repeated-run comparison — PR #74

- reusable face-level ground truth frozen from the completed baseline;
- exact candidate-source validation by filename and full SHA-256;
- deterministic IoU connected-component matching;
- exception review for unmatched, duplicate and ambiguous components;
- resumable private correction/gate storage; and
- overall, five-plus, source-group, category and M16 summaries.

### Review clarity and viewport — PRs #76, #77, #79 and #80

- one exception photo at a time;
- operator-facing candidate/reference decisions;
- automatic detector-miss handling when no candidate box exists;
- complete-image fitting with independent decision scrolling;
- zoom, pan and marker-to-decision linkage; and
- cross-catalogue image resolution using staged filename and full SHA-256 validation.

### Neutral out-of-scope detections — PR #92

- legitimate faces outside the frozen countable-face scope can be explicitly marked neutral;
- neutral cannot improve recall;
- neutral is excluded from the false-plus-duplicate gate; and
- existing private comparisons can be reopened and corrected without rerunning detectors.

## Completed WI-0037 CenterFace qualification

The exact upstream `centerface.onnx` file is pinned to `Star-Clouds/CenterFace@b82ec0c4844e89fd5a0305986aed9bdf33c72585` with:

- byte size `7,532,772`;
- Git blob SHA-1 `1487d5fe214feb569865b225216b24c8f4ef1050`; and
- SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`.

Runtime qualification retained the exact bytes and candidate parameters while:

1. replacing ONNX Runtime with upstream-compatible OpenCV DNN after stale static input metadata blocked dynamic photo tensors;
2. correcting N-D output marshalling; and
3. creating/disposal of a fresh OpenCV network per image after shared native network state corrupted later images in a batch.

The corrected disposable smoke was stable, and the subsequent governed 100-photo comparison passed the detector gate.

## Active WI-0038 rollout work

A detector-quality pass is not enough to point CenterFace at the canonical reviewed catalogue.

Existing face occurrences are stable identity anchors. Current persistence has historically used `(asset revision, ordinal)` as a uniqueness key, while each detector result is sorted anew. A different detector can therefore change which physical face occupies an ordinal. Reusing an old occurrence by ordinal alone could silently move an existing assignment/rejection to the wrong person.

PR #93 starts the safe-rollout foundation with:

- a versioned detector-pipeline SHA-256 identity that includes exact model bytes and material behavior such as confidence, runtime, preprocessing, resize/input-shape policy, NMS/top-K, tile/merge settings and rotation; and
- a conservative reconciliation planner that compares normalized boxes and all five landmarks, accepts only one-to-one eligible mappings, treats zero-match candidates as new faces and makes many-to-one/one-to-many mappings ambiguous.

The next WI-0038 slices must still:

1. persist the pipeline identity and reconciliation state;
2. prevent ordinal-only occurrence reuse in canonical writes;
3. preserve existing people, labels, rejections and append-only review actions;
4. expose ambiguous mappings for human review;
5. add genuinely new faces without overwriting old occurrences; and
6. run and roll back a pilot migration before full-archive canonical processing is authorised.

See [`docs/operations/detector-rollout.md`](../../operations/detector-rollout.md).

## Privacy

No private image names, source paths, face boxes, detailed counts, ground-truth files, databases, detector outputs or reconciliation decisions are committed. Repository evidence records only the fixed method, privacy-safe aggregate pass/fail conclusions, model/pipeline provenance and the active rollout boundary.
