---
id: M21
title: Reliability and recognition quality
status_source: ../status/milestones.yaml
depends_on: [M17, M19]
---

# M21: Reliability and recognition quality

## Outcome

Photo Identity remains usable as the real catalogue grows, and identity review remains trustworthy when images and reference data become harder and more numerous.

This milestone was created from three issues reported immediately after the successful 2026-08-26 consolidated M19/M20 maintainer verification. They are intentionally tracked as new follow-up work rather than weakening acceptance criteria that already passed.

## Priority order

1. **Critical — WI-0079:** included-folder synchronization is increasingly slow and recent browser requests fail with `net_http_request_timedout, 100`.
2. **High — WI-0080:** some face review images contain two visible faces, so the operator cannot always tell which face is the detection/review target.
3. **Medium — WI-0081:** identity suggestion accuracy appears to be degrading as the real review/reference corpus grows.

Priority controls investigation order, not a preselected implementation. Each work item must establish evidence and compare options before product-code changes begin.

## Work items

- [WI-0079](../work-items/WI-0079-included-folder-sync-timeout.md) — characterize included-folder synchronization scaling, request timeout/cancellation behavior and dominant runtime costs before selecting a correction.
- [WI-0080](../work-items/WI-0080-detected-face-clarity.md) — trace face geometry through review derivatives and select an accessible visual treatment that makes the detected face unmistakable when multiple faces are visible.
- [WI-0081](../work-items/WI-0081-suggestion-accuracy-degradation.md) — quantify suggestion-quality drift, audit reference/evidence quality and compare ranking/model mitigation strategies with false-positive safeguards.

## Investigation principles

- Diagnose before optimizing.
- Use the maintainer's real catalogue where necessary, but do not commit private photos, face crops, embeddings or identity data.
- Prefer measured phase/quality breakdowns over broad rewrites.
- Preserve immutable revision, OneDrive, review provenance and identity-audit safety contracts.
- Do not treat a larger HTTP timeout, a tighter crop or a different suggestion threshold as a complete solution without evidence that it addresses the underlying problem.
- Keep the three investigations separate enough that a fix in one area does not silently change another area's semantics.

## Exit criteria

- Included-folder synchronization has a measured scaling/root-cause explanation and an implemented correction that remains responsive/reliable on representative catalogue size.
- Review images make the target detected face unambiguous when another face is also visible, without changing identity evidence semantics.
- Suggestion-quality degradation is quantified and the chosen mitigation improves or restores representative accuracy without unacceptable false-positive regression.
- Each correction has focused regression/performance/evaluation evidence appropriate to its risk.
- Maintainer verification confirms the operator-facing behavior after implementation.
