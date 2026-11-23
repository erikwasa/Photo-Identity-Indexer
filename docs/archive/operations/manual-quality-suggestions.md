# Operator-controlled low-quality photo suggestions — first usable review workbench

WI-0629 / Issue #629. **A practical local review companion for the existing Photo Identity application**, using the accepted existing read-only `proxy-technical-quality-v1` analyzer. It is an intentionally bounded first product-experience phase, not a production automatic exclusion policy and not yet a native Blazor page.

## Why

The operator clarified that the goal is **automatic suggestions of photos worth considering for manual exclusion**, not automatic hiding or high-precision deletion. False positives are acceptable if review is convenient and **every final action is decided by the operator**. Distinguish this task from WI-0193's *NO-GO for unattended Very Low auto-exclusion* and WI-0194's *NO-GO for unattended near-duplicate suppression*. Neither no-go bans a human review queue.

The first version prioritizes suggestions of technical imperfections from existing bounded verified review proxies, with explainable possible blur, severe under/over-exposure, clipping, contrast and effective-resolution reasons. It does **not** claim to detect all noisy, badly framed, aesthetically poor, motion-blurred or repetitive photos; semantic redundancy suggestions and third-party model quality can be added as **separate optional detectors** once the manual workflow proves useful. The original WI-0182 300-photo sample had only ~13.8% *decisive technical-failure precision* among flagged cases, so a high proportion of “Keep”/“Dismiss” is expected. This is why it must **never automatically exclude**.

## Exactly what is implemented

1. **Generate a bounded, deterministic technical scan** using the existing .NET WI-0182 PostgreSQL **read-only** review-proxy evaluator. The default samples up to 1,000 current photos and verifies each existing JPEG derivative by SHA. Scan limit can be 1–5,000, with **no original file access**, no OneDrive hydration, no database mutation, no IQA weights and no requirement to rescore the full archive. The existing deterministic sample is NOT a whole-archive recommendation guarantee.
2. Create a private suggestion manifest with up to 500 candidate photos by default, limited to calibrated `jpeg-1600-q78` profile and known `proxy-technical-quality-v1` reason codes. Explainable ranking weights prioritize severe exposure over blur/low resolution over mild clipping/contrast; **weights are review-queue ordering only**, never an absolute quality grade or automatic exclusion threshold. Source paths/OneDrive source keys and any filled human benchmark reviews are **rejected from the suggestion output**, and reviews are not used to retrain the research models.
3. **Use a browser on `127.0.0.1:5188`**: full-width hash-verified existing JPEG preview, human-readable technical reasons, reason filter, *Keep*, *Dismiss suggestion*, *Consider exclusion*, previous/next/unreviewed navigation, keyboard 1–3, resumable privately stored human decisions, **Finish & Report**. Keep/Dismiss mark only the local suggestion queue. Consider exclusion is **an annotation** — it has **no API to exclude any photo**.
4. Three separate review contexts: **Archive cleanup** initially on, **Collection curation** off, **Slideshow curation** off. The operator toggles where the suggestion *queue* is useful, and the decisions for each context are independent. **These toggles do not affect production Archive, Search, Smart/Creative selection or slideshow playback.** Actual optional per-use-case hiding/filtering requires separately designed reversible, opt-in product behavior; don't confuse it with the context toggle in this pilot.
5. **Open in Photo Identity** links to the existing native `/photo/{revisionId}` page (app on `127.0.0.1:5080`). To *actually* exclude, the maintainer must use the **existing Photo Identity Exclude from Photo Identity action and its separate explicit confirmation**. As ADR-0008/WI-0091 specify, that does not delete OneDrive originals but **durably purges that source copy's local proxies/metadata/faces/identity data**. Restoring requires fresh processing: it is **not** the same as reversible hiding. No bulk exclude, zero native photo/catalogue mutations, and no silent near-copy suppression.
6. Private human decisions are bound to an immutable scored candidate manifest by SHA, saved atomically and resumed without overwriting history. Viewer only resolves existing hash-matched proxies under the configured root; malformed paths/hashes and human-labelled research inputs are rejected. Completion prints **aggregate-only per-context acceptance/dismiss/consider counts** with zero exclusions performed; do not share individual private photo IDs, paths, human decisions or review manifests.

## First local verification after CI and merge

Use the **same PowerShell session/configuration that succeeded in WI-0195** (existing local PostgreSQL connection and review-proxy root; no secrets in reports), after updating main.

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

# These must both be True without printing secret values
[bool]$env:PhotoIdentity__Postgres__ConnectionString
Test-Path $env:PhotoIdentity__ReviewProxyRoot

$env:PhotoIdentity__ReviewProxyProfileId = "jpeg-1600-q78"

# Stage 1: synthetic tests + read-only technical scan + suggestion preparation.
.\tools\PhotoIdentity.PhotoQualityEvaluation\run_manual_quality_suggestions.ps1
~~~

Expected: synthetic test suite `OK`, existing 1,000-photo bounded read-only technical-evidence summary, aggregate `wi0629-manual-quality-suggestions-v1` `candidatePhotos` and reason counts. No human review and no exclusions unless `-Review` is explicitly selected. The *first* run is potentially CPU/read-I/O intensive over 1,000 **already local** review-proxy files, and may create a private JSON file containing personal paths: never add `private/` files to Git.

If that seems worth reviewing, run:

~~~powershell
.\tools\PhotoIdentity.PhotoQualityEvaluation\run_manual_quality_suggestions.ps1 -Review
~~~

Your default Photo Identity application can remain running at `127.0.0.1:5080`. Browser review is locally accessible at `http://127.0.0.1:5188` and gives 3 choices. Start with a manageable *human* sample: e.g., first **20–50 suggestions** across the types that interest you. You need not review all 500. Use context switches to decide whether each use case adds value; they do **not** turn on any production filters. **Finish & Report** shuts down the local server and prints aggregate counts to PowerShell; rerunning `-Review` resumes. `Consider exclusion` marks an intention only. You can open a suggested photo in the native app and independently use the existing confirmed Exclude command if you personally want, but that's optional for evaluating the queue.

**Report back only:** synthetic test PASS/FAIL, source scanned count, number of suggestion photos, per-reason totals, aggregate per-context review decisions, and your verdict about false-positive burden and usefulness. Keep source keys, review proxy paths, revision IDs, per-photo notes and saved decisions local.

## Limits and next product milestone decision

This is the **first operator-facing candidate review UX**, not a whole-archive native Photo Identity-integrated feature. It is a local companion application with private files. The scan is a deterministic subset, not every photo in the archive. Current technical reasons miss some real blurry photos and flag many acceptable photos. It does not infer aesthetic quality or perform visual redundancy analysis. Separate per-use-case settings currently control **review preferences only**: saved collections, slideshows and archive visibility never change.

If the operator confirms the queue helps find unwanted photos, issue #629's next substantial product phase should move the UI into Photo Identity, add persisted local suggestion status and flexible scope preferences, and optionally layer other calibrated/locally licensed detectors. If it is not useful, revise candidate ranking or include other evidence types based on **manual suggestion value**, not based on the frozen WI-0193 held-out safety study. Preserve M37's conclusion about automatic exclusion as NO-GO while recording manual-suggestion GO/NO-GO/DEFER independently. Do not silently mark WI-0195's original four-arm automatic-policy goals as completed.
