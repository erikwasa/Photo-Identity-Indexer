---
id: M19
title: Photo metadata and semantic collections
status_source: ../status/milestones.yaml
depends_on: [M12, M14]
---

# M19: Photo metadata and semantic collections

## Outcome

The catalogue can organize photos using information beyond face identity: photographic capture metadata, location and automatic visible-content tags, with maintainer-owned manual tagging available as a fallback and correction path when automatic tagging is unavailable, misses a useful concept or produces an unusable result.

## Work items

- [WI-0056](../work-items/WI-0056-manual-photo-tags.md) — establish the canonical photo-tag representation and the manual fallback/correction controls in the photo viewer
- [WI-0049](../work-items/WI-0049-visible-content-tagging-experiment.md) — evaluate local automatic visible-content tagging against the canonical tag representation and select the primary production approach
- [WI-0050](../work-items/WI-0050-exif-smart-collections.md) — ingest EXIF capture metadata and create reusable smart collections from metadata, people and canonical tags

## Tag architecture

Automatic tagging is the intended primary tagging path for normal library use. Manual tagging is a fallback and correction mechanism, not the product baseline. WI-0056 is implemented first because automatic tagging still needs a stable canonical tag identity and a safe human recovery path before model selection; that implementation order does not imply that maintainers are expected to tag the archive manually.

Manual actions and model-produced evidence remain separate so rerunning or replacing a model cannot erase a maintainer intervention and a manual edit does not destroy reproducible model evidence. The production automatic-tag integration must define the effective-tag policy explicitly, including how an intentional manual correction or suppression for a specific tag takes precedence over conflicting automatic output.

WI-0049 remains evidence-driven: it measures candidate approaches before a model or automatic-evidence schema becomes a production dependency. M19 does not treat manual-only tagging as the desired end state. If the first experiment cannot identify an acceptable automatic approach, it must record the blocker and the next bounded experiment rather than treating the manual fallback as completion of the automatic-tagging goal.

## Initial automatic-tagging investigation boundary

The first experiment should use a controlled-vocabulary image/text similarity approach as the integration baseline because it maps directly onto canonical tags and fits the existing local ONNX/C# architecture. A purpose-built image tagger may be compared when its runtime and packaging cost are acceptable. Generative captioning is optional evidence, not a prerequisite for M19. The experiment also compares the existing durable review proxy with original-image inference before accepting any requirement to hydrate originals for semantic tagging.

## Exit criteria

- Capture time is stored as photographic local time when that is what the source metadata provides rather than being falsely normalized to UTC.
- GPS metadata is retained when available without making location mandatory.
- Automatic visible-content tagging has a selected production path, or M19 is explicitly blocked on a documented follow-up experiment rather than silently falling back to manual-only tagging.
- A maintainer can add and remove canonical tags without modifying original photos when automatic tagging needs human fallback/correction.
- Manual interventions and automatic tag evidence have distinct provenance and cannot silently overwrite each other.
- Smart collections can be saved and reevaluated as catalogue contents change and can include canonical tag predicates.
- The tagging experiment produces evidence about usefulness, runtime, storage, input-image requirements and model-governance implications before a production automatic model is selected.
