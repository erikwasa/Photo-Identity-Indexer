# Security and privacy

Face crops, embeddings and identity labels are sensitive biometric data.

Requirements:

- Personal OneDrive authentication remains inside the official Windows client.
- No OneDrive password or personal account token enters the application or Azure.
- Azure receives only explicit bundles; full originals are uploaded only when required.
- Prefer crop-only bundles for embedding comparisons.
- Protect bundles during transfer and keep temporary storage private.
- Short-lived SAS credentials, when used, must be narrowly scoped and never logged.
- Protect SSH keys locally.
- Delete temporary Azure inputs after verified result import.
- Keep the canonical SQLite database local and backed up.
- Use internal IDs rather than person names in logs where practical.
- Do not publicly expose the review API during early versions.
- Never modify original photos.
