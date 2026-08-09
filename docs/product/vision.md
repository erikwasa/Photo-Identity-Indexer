# Product vision

Photo Identity Indexer is a private, model-independent face and photo indexing system for a personal archive.

The system discovers photos, detects faces, creates reusable derivatives, generates replaceable embeddings, maintains people and identity decisions, suggests identities and supports collections over the resulting catalogue. Later capabilities extend that catalogue with capture metadata, location and visible-content tags.

The permanent centre of the system is:

```text
Authoritative local/OneDrive photo assets
    +
Stable canonical asset and face identity
    +
Auditable canonical people and review decisions
```

Canonical identity decisions are independent of any one recognition model. They may originate from a human review action or, when the accepted automatic-assignment policy is implemented and enabled, from a model-scoped automatic decision with complete provenance. Either way, later correction must preserve history rather than rewriting it invisibly.

OneDrive integration, Azure compute, recognition models, vector indexes, tagging models and user interfaces must remain replaceable. Original photos remain read-only.