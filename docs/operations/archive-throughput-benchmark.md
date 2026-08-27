# Archive throughput benchmark

This is the repeatable maintainer procedure for the WI-0076 performance investigation.

The benchmark must establish where archive wall-clock time is spent before Photo Identity changes
analysis concurrency, integrity verification, OneDrive hydration behavior, retries or model settings.
The instrumentation is deliberately process-local and aggregate-only. Do not commit benchmark
catalogues, source paths, filenames, photos, face data or raw logs.

## Benchmark build

Use the WI-0076 metrics-only package or a later build that retains the same diagnostics contract.
Record the exact application commit/package workflow used for every result.

The diagnostics endpoints are:

```text
GET  /api/archive/diagnostics/throughput
POST /api/archive/diagnostics/throughput/reset
```

The report contains only:

- reset/capture timestamps and an in-process generation number;
- aggregate stage count, total, average and maximum milliseconds;
- aggregate counters; and
- aggregate full-file SHA-256 read count/bytes plus subject-count/read-distribution statistics.

Opaque asset/revision keys may be used transiently in memory to calculate read distributions. They
are never included in the HTTP response. Metrics reset on application restart and can also be reset
explicitly with the endpoint above.

## Fixed media set

Use the same private archive folder for every comparison.

Prefer **100–200 mixed JPEG/HEIC images** so startup cost is amortized. Keep the files, models,
analysis profile, proxy profile, hydration settings, machine and power mode unchanged between
comparisons.

Use a disposable benchmark catalogue and disposable analysis/proxy directories outside OneDrive.
Do not reset the permanent catalogue merely to obtain a second benchmark.

## Scenario A — originals already local

1. Make every image in the selected folder locally available through OneDrive/Explorer.
2. Wait until no selected file is downloading or online-only.
3. Start with a fresh benchmark catalogue/output/proxy directory.
4. Configure only the selected benchmark folder as archive coverage.
5. Start Photo Identity and confirm the expected image count.
6. Reset metrics immediately before archive advancement:

```powershell
$Api = "http://127.0.0.1:5080"
Invoke-RestMethod -Method Post "$Api/api/archive/diagnostics/throughput/reset"
```

7. Record the starting status/storage snapshots:

```powershell
Invoke-RestMethod "$Api/api/archive/status"  |
    ConvertTo-Json -Depth 10 |
    Set-Content ".\wi-0076-local-status-before.json"

Invoke-RestMethod "$Api/api/archive/storage" |
    ConvertTo-Json -Depth 10 |
    Set-Content ".\wi-0076-local-storage-before.json"
```

8. Start unattended bounded archive advancement:

```powershell
$Started = Get-Date
Invoke-RestMethod -Method Post "$Api/api/archive/advance/start" | Out-Null
```

9. Poll `/api/archive/status` and `/api/archive/storage` every five seconds. Collect Windows
CPU, working-set and process I/O counters at the same cadence; CPU utilization is required WI-0076
evidence because it distinguishes serialized/idle behavior from a saturated inference workload.
Do not run other CPU/disk benchmarks while this test is active.
10. Stop timing when `status.advancement.state` becomes `complete` or `blocked`.
11. Save the final diagnostics:

```powershell
$Finished = Get-Date
$Elapsed = $Finished - $Started

Invoke-RestMethod "$Api/api/archive/diagnostics/throughput" |
    ConvertTo-Json -Depth 10 |
    Set-Content ".\wi-0076-local-throughput.json"

Invoke-RestMethod "$Api/api/archive/status" |
    ConvertTo-Json -Depth 10 |
    Set-Content ".\wi-0076-local-status-after.json"

"Elapsed=$Elapsed"
```

Do not reset the diagnostics until the JSON has been retained outside the repository.

## Scenario B — the same originals online-only

Repeat the exact procedure with a separate fresh benchmark catalogue/output/proxy directory after
using OneDrive **Free up space** on the same media set and waiting until the selected files are
online-only.

This scenario measures the complete bounded hydration/release path in addition to local processing.

Do not run Scenario A and Scenario B concurrently.

## Primary calculations

For each scenario record:

```text
images/hour   = analysed image count / elapsed hours
seconds/image = elapsed seconds / analysed image count
```

Also record:

- completed/failed image counts;
- peak managed hydrated/downloading/releasing/reserved bytes from storage polling;
- fraction of sampled time where archive advancement reports `waiting`;
- process CPU utilization and I/O rate if externally sampled; and
- all throughput diagnostics stages, counters and hash-read metrics.

## Diagnostics interpretation

Important stage names include:

- `synchronization`
- `onedrive-wait`
- `active-loop-delay`
- `source-verification`
- `source-verification-hash`
- `original-verification-hash`
- `metadata-inspection`
- `analysis-session-initialization`
- `analysis-session-lifetime`
- `analysis-source-hash`
- `image-decode`
- `face-detection`
- `face-alignment`
- `face-embedding`
- `face-persistence`
- `analysis-result-persistence`
- `review-proxy-generation`
- `face-review-derivative-generation`
- `hydration-request`
- `release-request`

Full-file hash-read kinds include:

- `synchronization`
- `source-verification`
- `original-status`
- `original-open`
- `analysis`

`SubjectCount`, `AverageReadsPerSubject` and `MaxReadsPerSubject` make redundant verification
visible without exposing which revision was read.

Stage timers can be nested (for example a session lifetime contains hashing, decode and inference),
so **do not sum every stage total and compare that sum with wall-clock elapsed time**. Use the
individual stages to identify dominant costs and use the separately measured benchmark elapsed time
for overall throughput.

## Decision rule

Do not select an optimization until both scenarios are reviewed.

Examples:

- repeated model-session initialization with material initialization time -> evaluate session reuse;
- several full-file reads per subject / hash bytes far above source bytes -> evaluate a safe verified-local lease;
- large `onedrive-wait` wall-clock share -> evaluate bounded hydration prefetch;
- detection/embedding dominates while CPU remains underused -> evaluate controlled batching/concurrency;
- proxy/face-review derivative stages dominate -> evaluate bounded post-analysis batching;
- only after larger costs are removed should the fixed active-loop delay be tuned.

Every optimization must be rerun against the same fixed media set and compared with this baseline.
