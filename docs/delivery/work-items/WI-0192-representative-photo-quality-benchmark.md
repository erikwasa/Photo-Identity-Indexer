---
id: WI-0192
title: Build a representative archive benchmark and blinded quality review workflow
milestone: M37
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Core.Tests, PhotoIdentity.Persistence.Tests, tools, docs]
---

# WI-0192: Build a representative archive benchmark and blinded quality review workflow

## Objective

Create a reusable, privacy-safe evaluation benchmark that represents the maintained archive well enough to judge absolute photo-quality models without repeating WI-0182's convenience-sample weakness.

## In scope

- Select an initial target of approximately 600 current photo revisions: roughly 400 representative archive photos plus roughly 200 challenge cases.
- Stratify the representative core across years, source devices/formats, resolution ranges, portrait/landscape, people/no-people, daylight/night/indoor conditions, scans/older material where present, and ordinary archive folders/content.
- Make the selection procedure deterministic and report per-stratum counts so representation can be inspected rather than assumed.
- Allow a second challenge pass to add model extremes, likely failure modes and candidate-model disagreement cases without replacing the representative core.
- Provide a blinded review workflow with `Very Low`, `Low`, `Normal`, `High`, `Very High` and `Unsure` labels plus a separate `personallyImportant` flag and optional notes.
- Define `Very Low` operationally as technically/presentationally poor enough that the maintainer would normally not want the photo selected automatically; do not equate it with deletion or personal worth.
- Keep model scores, candidate reasons and automatic tiers hidden during human labelling.
- Freeze a calibration/held-out partition before model tuning; keep known exact/near-identical photos in the same partition to avoid leakage.
- Export private labels/source keys only under private/data/staging or an external path. Commit only tooling, aggregate counts and privacy-safe protocol documentation.
- Make the benchmark/review format reusable by WI-0193 and the review infrastructure reusable by WI-0194 where practical.

## Checkpoint 1 implementation — representative core

The first implementation checkpoint introduces a provider-independent deterministic selector plus PostgreSQL population reader and a `benchmark` mode on the existing photo-quality evaluation tool.

The representative core defaults to 400 photos. It balances the marginal maintained-archive distribution across capture decade, camera/device family, media family, resolution bucket, orientation and detected-face count rather than taking one stable pseudo-random slice. Capture year uses extracted capture metadata first and can fall back to a year-prefixed source segment when metadata is absent.

The benchmark command reads only current `photo` revisions with the requested durable review-proxy profile. It records private source/proxy information only in an ignored private export. Selected proxy bytes are length/SHA-256 verified and receive the existing strict 64-bit dHash. Exact-content duplicates and selected dHash neighbors at Hamming <= 4 are forced into the same deterministic 60/40 calibration/held-out partition to reduce obvious leakage.

The JSON records population and selected coverage, the frozen policy/version identifiers and blank human-review fields. It deliberately reserves (by default) 200 challenge slots but does **not** populate them yet; the next checkpoint will add challenge selection and the blinded resumable review surface without replacing the representative core.

## Out of scope

- Selecting a production quality model.
- Persisting a quality score to the production catalogue.
- Automatic quality exclusion, Search reranking or collection behavior changes.
- Training a model on private photos.

## Acceptance criteria

- [ ] The benchmark selection reports coverage across the defined archive strata and is visibly more representative than the WI-0182 300-photo deterministic sample.
- [ ] Approximately 600 photos can be reviewed without exposing model predictions during labelling; a smaller final count is acceptable only with documented coverage evidence and rationale.
- [ ] Quality labels and `personallyImportant` are saved safely and resumably without modifying originals or production catalogue metadata.
- [ ] Calibration and held-out membership are deterministic/frozen before WI-0193 tuning, with duplicate/near-identical leakage controls.
- [ ] Private review material is excluded from Git and privacy-safe aggregate evidence can be reproduced.
- [ ] Documentation explains the label semantics, sampling method and how WI-0193/0194 consume the benchmark.

## Verification requirements

Automated sampling/export/resume tests plus maintainer inspection of stratum coverage and a short pilot review before the full benchmark is accepted.

## Completion notes

- Files changed: Core benchmark selection/split policy, PostgreSQL benchmark population query, photo-quality evaluation `benchmark` command, Core/Persistence tests and benchmark runbook.
- Trade-offs: checkpoint 1 freezes the representative core and split before adding challenge cases/review UX. Strict dHash is used only as a leakage guard, not as a claim that those photos are production visual duplicates.
- Deferred work: challenge tranche selection, blinded/resumable review UI and maintained-archive coverage acceptance remain in WI-0192.
- Commands run: repository CI for the checkpoint PR; maintained private benchmark generation follows after merge.
