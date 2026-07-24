# Recognition and identity matching

## Baseline

- Detector: YuNet through ONNX Runtime
- Embedder: SFace through ONNX Runtime
- Matcher: exact cosine similarity against human-confirmed exemplars

The initial candidate score is the maximum cosine similarity to a person's confirmed examples. Record the best and second-best people, both scores, their margin, face quality and model ID.

## Confirmation rules

- Only human-confirmed faces become exemplars initially.
- Automatic suggestions never train later suggestions.
- Rejected face-person pairs are retained.
- Suggestions are derived data and can be regenerated.
- Confirmed labels survive model changes.

## Improvement over time

Recognition can improve through more diverse examples, age and pose coverage, negative evidence, person-specific thresholds, prototype selection, quality filtering and rematching unknown faces.

Deep-model fine-tuning is deferred until application-level improvement has been measured and exhausted.
