# Initial scope

## First working version

The first version is a local vertical slice that runs on a Windows development computer.

It will:

1. Read a representative local folder of approximately 500–3,000 photos.
2. Detect faces and save padded and aligned crops.
3. Generate SFace embeddings.
4. Store metadata in SQLite.
5. Present faces in a local browser review interface.
6. Let the user create people and assign confirmed examples.
7. Suggest identities for other faces.
8. Let the user confirm or reject suggestions.
9. Produce an initial accuracy and performance report.

## Deferred from the first version

- Azure execution
- Microsoft Graph
- Azure application identities
- GPU requirements
- Public hosting
- Cloud databases
- Full processing of the 250 GB archive
- Videos
- Slideshows and collections
- Neural-network fine-tuning
- Automatic confirmation of suggestions

The first product question is whether the selected detector and embedder identify important people accurately enough in this specific archive.
