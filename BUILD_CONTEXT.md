# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0086 — Prepare and retain slideshow originals for best-quality playback** is implemented and in review on PR #220.

The slice adds an explicit full-snapshot best-quality preparation path without changing normal slideshow playback. Exact-head workflow #1337 passed after rerunning a transient governed-model download failure in package verification; build-and-test, both integration shards, launcher verification and the package retry are green. With Prepare originals Off, WI-0084's viewer-preview boundary remains authoritative and never hydrates an online-only original solely because playback reaches it.

With Prepare originals On, the immutable WI-0083 snapshot is protected as one ephemeral slideshow lease before hydration admission. The existing archive capacity service preflights aggregate additional bytes against the managed-byte limit and free-space reserve, requests only eligible non-session managed LRU releases, and waits until requested releases are observed online-only before the set is admitted. Existing per-revision hydration ownership/concurrency and immutable size/SHA-256 verification remain authoritative.

Slideshow protection is ephemeral rather than new durable ownership: leases expire unless the active browser heartbeats them, so abandoned/crashed sessions cannot permanently strand managed content. Already-local/user-pinned content is never claimed. On successful preparation, only the current image plus the existing bounded neighbor prefetch window uses session-scoped verified-original resources. Deliberate slideshow exit removes slideshow protection but does not force prepared app-owned files online-only; they return to ordinary managed-LRU eligibility.

Preparation failure pauses playback and presents an explicit parent choice to continue with normal available/proxy images or cancel preparation. A prepared original that later becomes unavailable or fails immutable verification also pauses and enters that fallback flow.

WI-0082 through WI-0085 remain `in_review` because consolidated real-device/product acceptance is intentionally deferred until WI-0086 is implemented. Their implementation dependencies are merged and satisfied.

WI-0076 remains separately recorded as `in_progress` and is not part of this M22 slice.

## Next concrete step

1. Merge PR #220 after the lifecycle-only status/evidence update remains green.
2. Perform the consolidated M22 maintainer review, including a real phone over the WI-0082 secure path and a mixed local/online prepared-original slideshow.
3. Complete M22 work items only after the maintainer acceptance evidence is recorded.

## Relevant files

- `docs/delivery/work-items/WI-0086-slideshow-original-preparation.md`
- `docs/product/slideshow.md`
- `src/PhotoIdentity.Api/ArchiveHydrationCapacityService.cs`
- `src/PhotoIdentity.Api/SlideshowOriginalLeaseRegistry.cs`
- `src/PhotoIdentity.Api/SlideshowOriginalPreparationService.cs`
- `src/PhotoIdentity.Api/SlideshowOriginalPreparationEndpoints.cs`
- `src/PhotoIdentity.Web/Pages/Slideshow.razor`
- `src/PhotoIdentity.Web/Pages/Slideshow.razor.cs`
- `src/PhotoIdentity.Web/SlideshowOriginalPreparationContracts.cs`
- `tests/PhotoIdentity.Integration.Tests/ArchiveHydrationCapacityServiceTests.cs`
- `tests/PhotoIdentity.Integration.Tests/SlideshowOriginalLeaseRegistryTests.cs`
- `tests/PhotoIdentity.Integration.Tests/SlideshowOriginalPreparationServiceTests.cs`
- `docs/delivery/status/work-items.yaml`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
