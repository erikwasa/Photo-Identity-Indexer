# Build context

Formal lifecycle status is resolved through `PhotoIdentity.Docs`; read only the target shard and linked work-item document before continuing.

## Current focus and next concrete step

WI-0174 implements explicit operator-authorized HTTPS caption-thumbnail inference under accepted ADR-0011, with Local remaining the default. Photo Identity only; no Azure infrastructure. Check its PR/CI, then perform the maintained Windows small remote benchmark/batch and return-to-Local acceptance in `docs/delivery/work-items/WI-0174-remote-caption-inference.md`. Matching model name/digest and caption policy must reuse remotely generated evidence without transport-only regeneration. Do not complete the item before this acceptance is recorded.

Independent continuation pointers: WI-0170/PR #471 requires bulk face commit/refill acceptance. M32 slideshow follow-ups from PR #470 require the preparation, Creative scale and navigation/management checks recorded in WI-0165, WI-0169, WI-0173 and WI-0172; WI-0171 phone artifact reproduction and WI-0167 OneDrive-unavailable acceptance remain separate. WI-0163 bulk metadata enrichment is independently in progress; do not change its lifecycle here. M30 video remains deferred.

## Relevant pointers

- docs/delivery/work-items/WI-0174-remote-caption-inference.md
- docs/decisions/ADR-0011-operator-authorized-remote-caption-inference.md
- docs/operations/local-caption-narration-evaluation.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
