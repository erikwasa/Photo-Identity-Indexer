# Product vision

Photo Identity Indexer is a private, model-independent face-indexing system for a personal photo archive.

The system will discover photos, detect faces, create reusable face crops, generate replaceable embeddings, let the user associate faces with named people, suggest identities, and record confirmations and rejections.

The resulting person-photo index will later support albums, collections, slideshows, multi-person searches, additional tags and date- or event-based selections.

The permanent centre of the system is:

```text
Local photo assets
    +
Canonical face occurrences
    +
Human-confirmed people labels
```

OneDrive integration, Azure compute, recognition models, vector indexes, clustering algorithms and user interfaces must remain replaceable.
