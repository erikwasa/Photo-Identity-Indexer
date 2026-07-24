# Model manifests and governance

Every detector, aligner and embedder must have an immutable descriptor containing:

- Stable model ID and role
- File format and SHA-256
- Source and version
- Input dimensions and colour order
- Normalisation and alignment protocol
- Output dimensions and normalisation
- Distance metric
- Runtime compatibility
- Code and weight licences

A model identity changes when weights, preprocessing, alignment, input dimensions, quantisation or material runtime behaviour changes.

Do not assume a repository licence automatically applies to downloaded pretrained weights. Record code, model-file and training-data considerations separately.

Large models must not be committed. Download tooling should verify checksums into an ignored local directory.
