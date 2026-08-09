# Non-goals

The current product direction deliberately does not aim to:

- Modify original photos or write identity, tag or catalogue metadata back into them.
- Identify public figures or perform general internet-scale face recognition.
- Provide live-camera, surveillance or real-time identification.
- Depend on hosted recognition APIs whose embeddings or model provenance cannot be retained locally.
- Train or fine-tune a neural network as the default way to improve results.
- Require Microsoft Graph, tenant administrators, app registrations, service principals or managed identities for Personal OneDrive access.
- Keep the canonical catalogue or review application permanently in Azure.
- Require the full logical OneDrive archive to remain hydrated on local disk.
- Support several simultaneous independent writers to the canonical catalogue.
- Guarantee universal decoding of every proprietary camera RAW format ever produced; version 1 targets HEIC/HEIF and the RAW variants actually present in the maintained archive and reports unsupported variants explicitly.
- Process video in version 1.
- Build a public cloud photo gallery or public sharing service.
- Treat model-generated visible-content tags as authoritative human facts without provenance.

Automatic identity assignment is **not** a non-goal anymore. The accepted direction is to support optional canonical automatic assignment for configured High-confidence matches, with explicit audit provenance and manual correction.