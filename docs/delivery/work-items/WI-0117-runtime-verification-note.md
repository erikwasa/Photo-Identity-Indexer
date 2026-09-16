# WI-0117 runtime verification note

During maintainer verification after PR #343 merged, a real match-regeneration run failed during multi-evidence automatic-assignment finalization with:

`Multi-evidence automatic assignment requires an active exact-model regeneration run.`

The regeneration hosted service invokes automatic assignment before completing the active run, so the lifecycle ordering was correct. The defect was the PostgreSQL `requested_at_utc` read inside `PostgresIdentityAutoAssignmentService`: `ExecuteScalarAsync` returned provider-shaped timestamp data and the code required the untyped scalar object to already be `DateTimeOffset`.

The follow-up fix reads the `timestamptz` field through `NpgsqlDataReader.GetFieldValue<DateTimeOffset>()`, matching the repository's established typed timestamp-read pattern. A live-PostgreSQL regression test seeds a running exact-model regeneration with multi-evidence enabled and verifies automatic-assignment evaluation does not falsely reject the active run.

WI-0117 remains `in_review` until the maintainer reruns regeneration on the patched build and completes the representative multi-evidence history/correction/undo verification.
