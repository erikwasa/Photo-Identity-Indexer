# Azure tenant and identity constraints

The photo library is in personal OneDrive. Azure access is governed by an enterprise tenant that restricts app registrations and application identities.

The design must not require:

- Microsoft Entra app registrations
- Service principals
- Managed or workload identities
- Federated credentials
- Client secrets or certificates
- Microsoft Graph permissions in the enterprise tenant
- An Azure-hosted process authenticating to personal OneDrive

The Windows computer is the trusted bridge. Azure receives only explicit processing bundles and returns results. Interactive Azure CLI, PowerShell, Portal, Storage Explorer or AzCopy access may be used by the user from the local computer.

## Execution without application identities

The worker's interface is portable:

```text
photoidentity-worker run --input <bundle-path> --output <result-path>
```

It requires no Azure SDK, Azure login, tenant metadata, service principal or managed identity. No Azure process reads OneDrive or the canonical database.

Transfer modes from the retired VM strategy (historical; see ADR-0004 and ADR-0013 for the current position):

- **SSH/SCP:** provision a temporary VM interactively, copy a small bundle by SCP, run the worker, copy the result back, import locally and deallocate the VM.
- **Short-lived SAS (optional):** if policy permits, the local user may create a narrowly scoped, time-limited SAS for one private job container. The credential must never be logged and expires or is revoked after result retrieval.

## Cost controls

Before processing:

1. Keep the subscription spending limit where available.
2. Create a project resource-group budget and alerts.
3. Tag every resource.
4. Keep VMs deallocated by default.
5. Use the smallest suitable disks and avoid unnecessary public resources.
6. Limit worker runtime, bundle size, asset count and estimated spend.
7. Require explicit commands for upload and full-library runs.
8. Reserve part of the $50 monthly credit for storage, retries and mistakes.
9. Prefer face-crop bundles when comparing embedders.
10. Delete temporary cloud data after verified import.

After each pilot, project full cost from measured end-to-end throughput and apply a safety factor. A full run must stop, or be split across later months, when it would exceed the configured budget.
