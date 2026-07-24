# Azure cost controls

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

After each pilot, project full cost from measured end-to-end throughput and apply a safety factor. The full run must stop or be divided into later months when the configured budget would be exceeded.
