---
id: ADR-0006
title: Allow configurable canonical automatic identity assignment
status: accepted
date: 2026-08-09
supersedes: [ADR-0005]
superseded_by: []
---

# ADR-0006: Allow configurable canonical automatic identity assignment

## Context

The permanent archive will contain far more faces than the initial review pilot. Requiring a human acceptance click for every strong identity suggestion would make review throughput the limiting factor even when matching evidence is consistently reliable.

The maintainer has explicitly chosen a new direction: sufficiently strong matches may become canonical assignments automatically when that behavior is enabled.

## Decision

Add an exact-model matching policy with configurable confidence groups and an explicit automatic-assignment toggle.

When automatic assignment is enabled, a qualifying High-confidence top suggestion may create a normal canonical identity assignment without a human acceptance click.

Every automatic assignment must record the exact model revision, score and policy/threshold evidence that caused it. Automatic assignments participate as exemplars in **later** matching runs.

Each regeneration scores its targets from one fixed exemplar snapshot before applying automatic decisions. A newly automatic assignment therefore cannot cascade into more automatic assignments inside the same regeneration run.

A later manual reassignment supersedes the automatic assignment through normal append-only history. The manually selected identity becomes the active assignment and therefore the exemplar identity used by later regeneration.

Changing thresholds affects future decisions only; it does not silently rewrite historical canonical assignments.

Automatic assignment remains disabled by default until the operator deliberately enables it after observing real results.

## Consequences

Canonical assignment provenance must distinguish human and automatic actors. Matching can improve faster as the permanent catalogue grows, but threshold tuning and audit visibility become safety-critical product controls.

ADR-0002 remains in force: canonical identity is model-independent even though a model-scoped policy may create an assignment. ADR-0005 is superseded because human confirmation is no longer mandatory for every canonical assignment.