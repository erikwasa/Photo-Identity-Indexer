---
id: WI-0095
title: Retry transient governed-model download interruptions
milestone: M22
status_source: ../status/work-items.yaml
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Recognition.Onnx, PhotoIdentity.Models, packaging, testing]
---

# WI-0095: Retry transient governed-model download interruptions

## Objective

Prevent otherwise healthy Windows package verification from failing when a governed ONNX model download is interrupted by a transient HTTP/stream transport failure.

This corrective item was created after main workflow #1356 failed in package verification with:

    error: The response ended prematurely. (ResponseEnded)

Build-and-test, both integration shards and launcher verification passed. The failure occurred while installing governed model files for the package build, before packaged startup verification.

## Contract

Model installation should retain the existing integrity and atomic-install guarantees while adding bounded retry for transient transport failures.

V1 behavior:

- attempt each missing/invalid governed model download up to three times;
- retry only transport-level failures such as HttpRequestException or IOException while reading the response body;
- do not retry cancellation;
- do not retry HTTP success responses that fail governed size/SHA-256 integrity verification;
- discard any partial temporary file before a retry;
- never promote a partial or unverified file to the governed model path;
- a valid already-installed model still performs no network request;
- after the final transient failure, surface the original failure normally so CI remains visibly red.

The retry belongs in ModelInstaller so package verification, local model installation and other callers receive the same governed behavior rather than adding CI-only retries.

## Acceptance criteria

- [ ] A transient first download failure followed by a valid response installs the governed model successfully.
- [ ] Repeated transient failures stop after three attempts and leave no destination or partial download file.
- [ ] Integrity mismatch is not retried and is never promoted.
- [ ] Existing valid-file no-download behavior remains unchanged.
- [ ] Cancellation is not converted into retries.
- [ ] Package verification continues to exercise the real model installer and does not bypass governed manifests or verification.
- [ ] Required CI passes on the corrective PR.

## Non-goals

- Resumable/range downloads.
- Changing governed model URLs, hashes or sizes.
- Retrying arbitrary application/runtime failures.
- Weakening package verification.
