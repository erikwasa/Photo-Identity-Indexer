# Success criteria

The initial hypothesis is validated when:

- At least five important people can be created and enrolled with several confirmed examples.
- The system suggests identities for previously unseen faces.
- High-confidence suggestions reach approximately 99% precision on the reviewed evaluation sample.
- Incorrect suggestions can be rejected and are not immediately repeated.
- Adding confirmed examples improves later suggestions.
- Interrupted processing resumes safely.
- Reprocessing does not duplicate assets, faces or embeddings.
- A second model can be added without changing canonical people or labels.
- Model-specific embeddings can be regenerated.
- Original photos remain unchanged.
- The same processing bundle can run locally or in Azure.
- Azure processing requires no application identity.

Precision is more important than recall. An unidentified face is preferable to a confidently incorrect identity. The numerical target may be revised after real measurements.
