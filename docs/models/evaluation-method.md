# Model evaluation method

## Evaluation subset

Use approximately 1,000–3,000 representative photos and 3,000–10,000 labelled faces. Include frequent and unknown people, groups, small faces, low light, profiles, age changes, similar relatives and scanned photos.

## Detector metrics

- Face recall and false detections
- Recall by face size and photo category
- Runtime, memory and CPU/GPU throughput

## Embedding and identification metrics

- Same-person and different-person similarity distributions
- Top-one precision and recall
- Unknown-person false acceptance
- Confusion between relatives
- Performance across age gaps
- Precision at proposed confidence thresholds
- Runtime per face

## Execution order

1. Detect faces locally once.
2. Review canonical face occurrences.
3. Store reusable crops.
4. Package identical crops for candidate embedders.
5. Run models locally or in Azure.
6. Import embeddings.
7. Compare all models against identical labels.

Use gallery, validation and test splits. Validation chooses thresholds; the test split reports final metrics.

Do not process the full archive until accuracy, licence, throughput, local/Azure consistency and projected cost are acceptable.
