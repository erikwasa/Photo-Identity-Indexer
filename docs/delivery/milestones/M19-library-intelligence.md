---
id: M19
title: Photo metadata and semantic collections
status_source: ../status/milestones.yaml
depends_on: [M12, M14]
---

# M19: Photo metadata and semantic collections

## Outcome

The catalogue can organize photos using information beyond face identity: photographic capture metadata, location, maintainer-owned photo tags and, when a viable local approach has been established, automatic visible-content tag evidence.

## Work items

- [WI-0056](../work-items/WI-0056-manual-photo-tags.md) — establish the canonical photo-tag representation and manual tagging in the photo viewer
- [WI-0049](../work-items/WI-0049-visible-content-tagging-experiment.md) — evaluate local automatic visible-content tagging against the canonical tag representation and record a production recommendation
- [WI-0050](../work-items/WI-0050-exif-smart-collections.md) — ingest EXIF capture metadata and create reusable smart collections from metadata, people and canonical tags

## Tag architecture

Manual tagging is the production baseline rather than a fallback for automatic tagging. Canonical tag identity and human assignment history are established before the model experiment. Model-produced evidence remains separate and carries exact model/score provenance so rerunning or replacing a model cannot overwrite a maintainer assignment.

Automatic tagging remains evidence-driven: WI-0049 measures candidate approaches before any model becomes a production dependency. WI-0050 consumes canonical tags regardless of whether automatic tag evidence has been selected by then.

## Initial automatic-tagging investigation boundary

The first experiment should use a controlled-vocabulary image/text similarity approach as the integration baseline because it maps directly onto canonical tags and fits the existing local ONNX/C# architecture. A purpose-built image tagger may be compared when its runtime and packaging cost are acceptable. Generative captioning is optional evidence, not a prerequisite for M19.

## Exit criteria

- Capture time is stored as photographic local time when that is what the source metadata provides rather than being falsely normalized to UTC.
- GPS metadata is retained when available without making location mandatory.
- A maintainer can add and remove canonical tags without modifying original photos.
- Manual tag assignments and automatic tag evidence have distinct provenance and cannot silently overwrite each other.
- Smart collections can be saved and reevaluated as catalogue contents change and can include canonical tag predicates.
- The tagging experiment produces evidence about usefulness, runtime, storage and model-governance implications before a production automatic model is selected.
