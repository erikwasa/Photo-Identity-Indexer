# Archive background synchronization

Archive synchronization initiated from the browser runs as durable server-side background work. This avoids coupling a full permanent-archive scan to one browser HTTP request.

## Why this changed

The Archive page previously executed **Sync included folders** inside `POST /api/archive/sync`. On the maintainer catalogue, the optimized scan still needed longer than the browser client's roughly 100-second request lifetime. The server continued making useful progress, but the browser cancelled the request at about 100 seconds and surfaced `net_http_request_timedout, 100`.

The existing incremental sync optimization remains in place. Unchanged files are not re-hashed unnecessarily; moving the browser action to background execution fixes the request-lifetime boundary rather than weakening source verification or increasing the browser timeout.

## Browser behavior

**Archive -> Sync included folders** now calls:

```text
POST /api/archive/sync/start
```

The endpoint queues synchronization and returns `202 Accepted` immediately. The existing `ArchiveAdvancementHostedService` performs the actual scan with the application lifetime rather than the browser request lifetime.

The Archive page polls lightweight archive status while work is active. Public synchronization states are:

- `queued` — the request is persisted and waiting for the background worker;
- `syncing` — included folders are being synchronized;
- `sync-complete` — synchronization completed successfully; and
- `blocked` — synchronization stopped on a non-retryable error, with the durable status message describing the failure.

Refreshing, navigating away from the Archive page, or a temporary browser disconnection does not cancel queued synchronization. Returning to Archive reads the persisted state and current catalogue counts.

A standalone sync stops after synchronization. It does **not** automatically start archive analysis, managed hydration, proxy generation, or other advancement work.

## Coordination with archive advancement

Full **Advance archive** already owns its synchronization step. To avoid two scans mutating archive state concurrently:

- a standalone sync cannot be started while full archive advancement is running;
- full archive advancement cannot be started while standalone synchronization is active; and
- Archive coverage controls and duplicate sync actions are disabled while standalone synchronization is active.

After `sync-complete`, normal Archive actions are available again.

## API compatibility

`POST /api/archive/sync` remains synchronous and still returns the completed `ArchiveSyncResponse`. It is retained for operator scripts, package verification and other non-browser compatibility callers that intentionally wait for the complete result.

Browser/lifecycle callers should use `POST /api/archive/sync/start` instead. New UI code must not move long-running permanent-archive synchronization back into a browser request solely by increasing `HttpClient.Timeout`.

## Persistence and restart behavior

Sync-only intent uses the existing archive advancement control record. PostgreSQL continues to store only the established `desired_state` values `running` and `paused`; sync-only execution is represented by runtime substates and therefore requires no catalogue schema migration.

Because the request is persisted, normal application restart/retry handling can observe outstanding sync-only work rather than depending on the original browser connection. The worker completes the request by transitioning to the durable `sync-complete` state.

## Troubleshooting

If the Archive page remains at `queued` for more than a normal worker-start interval, check that the API host is healthy and that `ArchiveAdvancementHostedService` is running. In production composition the hosted service is registered by the API host.

If the state reaches `blocked`, inspect the durable status message and launcher/API logs. Do not diagnose a `blocked` state as the former browser timeout unless logs actually show request cancellation at the client lifetime boundary.

The historical failure that motivated this change had these characteristics:

- all included-folder work was performed inside one browser request;
- repeated scans still showed the incremental path working (`hashed_files=0` for unchanged coverage);
- the request ended at about 100 seconds with HTTP 499/client cancellation; and
- retrying simply repeated the same request-lifetime failure.

With background synchronization, a scan may legitimately run for longer than 100 seconds without being cancelled by the browser.
