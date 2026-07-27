# Model evaluation method

## Two evaluation scales

### Local acceptance pilot

Use approximately 450–550 representative photos to test whether the complete product workflow is usable: processing, restart and resume, browser review on Windows and Pixel, people maintenance, suggestion handling, catalogue export, queries, backup and recovery.

This pilot is intended to find integration and usability defects. It is not sufficient by itself to claim production accuracy.

### Production model evaluation

Before selecting a production model, expand to approximately 1,000–3,000 representative photos and 3,000–10,000 labelled faces when the private archive supports it. Include frequent and unknown people, groups, small faces, low light, profiles, age changes, similar relatives and scanned photos.

## Detector metrics

- Face recall and false detections
- Recall by face size and photo category
- Runtime, memory and CPU/GPU throughput
- Review effort caused by missed or spurious detections

## Embedding and identification metrics

- Same-person and different-person similarity distributions
- Top-one precision and recall
- Unknown-person false acceptance and rejection
- Confusion between relatives
- Performance across age gaps
- Precision at proposed confidence thresholds
- Runtime and storage per face
- Operator effort to accept, reject or correct suggestions

## Execution order

1. Prove the baseline workflow on the 500-image local pilot.
2. Review canonical face occurrences and people through the browser application.
3. Export deterministic gallery, validation and held-out test splits from the reviewed catalogue.
4. Retain reusable aligned crops and immutable source revision identifiers.
5. Add the candidate model without changing canonical people or labels.
6. Process the same source revisions for every model.
7. Compare all compatible models against identical labels and splits.
8. Review representative disagreements manually.
9. Use Azure only later to validate execution consistency and cost, not to redefine labels.

Validation chooses thresholds; the held-out test split reports final metrics. Do not tune the threshold grid after inspecting test results.

Do not process the full archive until accuracy, licence, throughput, local/Azure consistency and projected cost are acceptable.
