# Success criteria

The initial product hypothesis is validated when:

- A representative 450–550 image subset completes the local workflow from scanning through review and evaluation.
- At least five important people can be created and maintained with several confirmed examples.
- The browser workflow is practical on Windows and Pixel over a trusted private network.
- Faces can be assigned, rejected and undone; people can be renamed or merged; repetitive review can use safe bulk actions.
- The system suggests identities for previously unseen faces and exposes score, margin and model provenance.
- High-confidence suggestions approach the agreed precision target on the reviewed evaluation sample.
- Incorrect suggestions can be rejected and are not immediately repeated.
- Adding confirmed examples improves later suggestions without automatically changing labels.
- Interrupted processing resumes safely and reprocessing does not duplicate assets, faces or embeddings.
- A reviewed catalogue can produce reproducible gallery, validation and held-out test data.
- A second model can be added without changing canonical people, labels or review history.
- Model-specific embeddings and suggestions coexist and can be compared on the same corpus.
- Collection queries produce expected confirmed-only results and neutral exports.
- Original photos remain unchanged.
- A new operator can follow the documentation from a clean setup.
- The same processing bundle can later run locally or in Azure without giving Azure an application identity or OneDrive credential.

Precision is more important than recall. An unidentified face is preferable to a confidently incorrect identity. A 500-image pilot proves integration and usability; final model selection requires broader held-out evidence when available.
