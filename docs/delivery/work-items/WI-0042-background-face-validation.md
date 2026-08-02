---
id: WI-0042
title: Validate unknown and background face handling
milestone: M17
status_source: ../status/work-items.yaml
depends_on: [WI-0041]
affected_modules: [PhotoIdentity.ReviewVerification, Evaluation, Documentation]
---

# WI-0042: Validate unknown and background face handling

## Objective

Prove that a higher-recall detector does not create unbounded identity work when many detected people are unknown or irrelevant.

## Scope

- Use the detector-recall sample and a representative group-photo subset.
- Measure named, unknown, ignored, false-detection and deferred outcomes.
- Measure review interactions and time before and after the non-identity workflow.
- Verify suggestion, progress, collection and evaluation filtering.
- Verify undo, reassignment and later naming of an unknown person.

## Acceptance criteria

- [ ] Every detected face can reach a valid terminal or deferred state without creating a named identity.
- [ ] Background / ignore and not-a-face outcomes are excluded from normal identity queues and matcher inputs.
- [ ] Unknown-person faces can be named later without losing occurrence, crop or audit history.
- [ ] Progress counts clearly distinguish detected, reviewed, named, unknown, ignored, false and deferred faces.
- [ ] Collections remain based on named identities unless explicitly configured otherwise.
- [ ] Private-pilot evidence shows materially reduced review burden for background-heavy photos.
- [ ] Only privacy-safe aggregate evidence is committed.
