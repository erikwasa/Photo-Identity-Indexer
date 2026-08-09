# Risks and mitigations

| Risk | Mitigation |
|---|---|
| OneDrive placeholders are visible but authoritative bytes are not local | Track availability separately from source-verification state; hydrate only through the bounded managed path when bytes are actually required |
| OneDrive dehydrates or changes a source after it was catalogued | Retain lightweight observations, require SHA-256 re-verification when observations diverge, and never create immutable revisions from metadata alone |
| The logical archive is larger than safe local free space | Keep originals authoritative in OneDrive, use durable review proxies, enforce free-space reserve/managed-byte/concurrency limits and release only Photo-Identity-owned hydration |
| A release request is mistaken for immediately free capacity | Count releasing content against the managed budget until OneDrive reports it online-only |
| Folder expansion creates duplicate processing | Keep one stable archive source identity, normalize relative included folders, let parents subsume children and reuse completed exact-profile revisions |
| Renames or moves appear as unrelated assets | Reconcile source observations with content identity and stable catalogue records instead of relying only on paths |
| HEIC/HEIF behavior differs across decoder/runtime environments | Isolate decoding, pin the chosen implementation and verify representative archive samples through the same processing path used by production |
| Proprietary RAW variants differ or are expensive to decode | Inventory the real archive first, support every RAW variant actually present, test representative files and report unsupported/corrupt variants explicitly rather than silently skipping them |
| RAW orientation/color/embedded-preview behavior changes face-detection quality | Define one deterministic rendered-RGB contract and compare representative RAW outputs visually and through detector smoke tests before accepting the decoder |
| Automatic identity assignment creates a feedback loop | Keep it disabled by default, scope thresholds to an exact model policy, score from a fixed exemplar snapshot, record complete automatic provenance and let manual reassignment supersede the decision |
| Similar relatives receive confident wrong matches | Tune conservative High thresholds from reviewed evidence, expose confidence groups and retain easy manual correction/audit history |
| Children and long time spans shift appearance | Keep age-diverse exemplars and consider temporal/quality-aware prototypes only after archive-scale evidence shows a need |
| Model scores are compared across incompatible revisions | Version embeddings, suggestions and thresholds by exact model ID/hash and never silently mix score scales |
| A detector change changes the face population | Treat detector rollout as a governed migration, preserve face/review history where reconciled and rerun embedding evaluation on the changed population before reaffirming production-model conclusions |
| The SQLite catalogue is corrupted or copied inconsistently | Keep it on local disk, use short transactions and quiesced backup/restore with integrity and foreign-key checks |
| Browser access exposes private identity data on the LAN | Bind to localhost by default; use only a trusted private network when another device must connect and keep firewall scope narrow |
| Review proxies become mistaken for authoritative source images | Keep proxy profile/version metadata explicit and use authoritative original bytes for analysis and explicit full-resolution viewing |
| Semantic tagging produces misleading or unstable labels | Treat model-generated tags as derived model-scoped evidence, retain confidence/provenance and evaluate usefulness before selecting a production tagging model |
| Azure examples become an accidental product dependency | Keep Azure optional, use portable bounded bundles only and keep canonical data/OneDrive credentials on the local control plane |
| Temporary Azure compute or transfer credentials are lost/leaked | Use small resumable jobs, narrow short-lived credentials, never log secrets and delete/deallocate temporary resources promptly |