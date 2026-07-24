# Azure execution without application identities

The worker's fundamental interface is portable:

```text
photoidentity-worker run --input <bundle-path> --output <result-path>
```

It requires no Azure SDK, Azure login, tenant metadata, service principal or managed identity.

## First transfer mode: SSH/SCP

Provision a temporary VM interactively, copy a small bundle by SCP, run the worker, copy the result back, import locally and deallocate the VM.

## Optional transfer mode: short-lived SAS

If policy permits, the local user may create a narrowly scoped, time-limited SAS for one private job container. The credential must never be logged and expires or is revoked after result retrieval.

## Execution phases

1. Local-only development and validation
2. Manual temporary VM pilot
3. Repeatable PowerShell wrappers using interactive Azure CLI context
4. Optional Blob checkpointing
5. Bounded full-archive batches spread across monthly credit periods

No Azure process reads OneDrive or the canonical database.
