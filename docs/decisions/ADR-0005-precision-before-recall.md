---
id: ADR-0005
title: Optimise for precision before recall
status: superseded
date: 2026-07-24
supersedes: []
superseded_by: [ADR-0006]
---

# ADR-0005: Optimise for precision before recall

## Context

Incorrect identities contaminate later searches and can create feedback loops. Unknown faces remain reviewable.

## Decision

Use conservative thresholds and require human confirmation during early versions. Only human-confirmed faces may become exemplars.

## Consequences

Initial recall may be lower, but the canonical people index remains trustworthy and can improve through additional examples.

## Supersession

ADR-0006 supersedes the mandatory-human-confirmation part of this decision. The product now permits optional canonical automatic assignment for configured High-confidence matches. Conservative thresholds and careful precision tuning remain important, but a human click is no longer required for every canonical assignment.