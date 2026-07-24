# Risks and mitigations

| Risk | Mitigation |
|---|---|
| OneDrive placeholders appear but content is unavailable | Track availability and process only verified hydrated or staged content |
| Renames or moves appear as new assets | Reconcile using size and content fingerprints |
| OneDrive de-hydrates files | Stage active inputs outside the sync folder |
| Enterprise examples assume cloud identities | Use interactive control, SCP or short-lived SAS only |
| Azure worker cannot access canonical data | Make bundles self-contained and imports idempotent |
| Temporary VM is lost | Use small jobs, checkpoints and prompt result retrieval |
| SAS credential leaks | Narrow permissions and lifetime; never log; delete temporary containers |
| HEIC differs across Windows and Linux | Isolate decoding and validate actual formats on both platforms |
| New model embeddings are incompatible | Version embeddings by model and preserve crops and labels |
| Incorrect automatic label contaminates matching | Only human-confirmed labels become exemplars initially |
| Similar relatives are confused | Use conservative thresholds and best-versus-second-best margins |
| Children change with age | Keep age-diverse exemplars and later add temporal prototypes |
