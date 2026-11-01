# WI-0190 checkpoint 15 — bounded four-frame selected one-shot

The previously verified WI-0190 real run published **two** complete
frame captions and 512-dimensional CLIP vectors. The selected one-shot
was capped at two frames even though the v38 transactional publisher,
sampling planner and scene-policy identity already supported four.
This checkpoint makes **1–4 frames** available per *explicitly
operator-selected* job, for interval or manual scene sampling.

**Interval provenance:** the historical two-frame interval identity
remains video-frame-sampling-v1, preserving already-published jobs.
Previously the legacy interval key did not encode the selected
frame count. This checkpoint makes new **1-, 3- and 4-frame**
interval attempts use video-frame-sampling-interval-v2-n1/n3/n4,
so requesting four on a previously published two-frame clip cannot
reuse a succeeded two-frame job by accident. The historical policy
remains readable with its persisted requested_frames value.
Annotated-scene identities already encode the count and private
scene cuts. No migration or retroactive relabeling is done.

It does not activate automatic processing. Selecting four is a fresh
authorization decision: up to four sampled private JPEG frames may be
sent to the configured vetted loopback caption service, which may in
turn be bridged to the operator's Azure infrastructure. It may
increase time and billing. A successful prior two-frame job is not
reprocessed, rekeyed or invalidated.

## Unchanged operational bounds

- Only one already-current locally published and SHA-verified SDR proxy
  selected by its PlayableProxy job UUID; no original-video opening,
  OneDrive hydration, unattended discovery or external inference route.
- The default is still a **dry-run** with no enqueue/model/FFmpeg.
  Execution requires --apply plus both independent explicit v38
  and private-frame-disclosure acknowledgments.
- **13-minute total attempt** (780 seconds) and **15-minute absolute
  authorization window** are unchanged, along with one-job
  concurrency, photo priority, cancellation, source-revision/exclusion
  checks, v38 complete-batch commit, model digest pinning and
  lease heartbeat.
- Per-frame FFmpeg remains supervised and capped at 60 seconds,
  with a separate maximum 180-second inference budget. The full
  owned scratch reservation remains 320 MiB, with the checkpoint
  14 locked-workspace crash recovery path.
- Human scene cuts are private, canonical, strictly increasing and
  inside the video duration. Four requested frames **must** produce
  four valid distinct timestamps, else preflight returns
  invalid-sampling-plan before enqueueing or running models.

**Four-frame timeout caveat:** four worst-case FFmpeg extractions
and four worst-case inference calls could total 16 minutes, exceeding
the unchanged 13-minute attempt. The global deadline intentionally
wins: slow attempts may cancel and must **not publish partial
evidence**. This checkpoint does not silently widen authorization
or permit longer jobs.

## Windows PowerShell read-only preflight

Follow the existing
[selected one-shot runbook](video-frame-evidence-selected-one-shot.md)
to build a pinned local FFmpeg/CLIP/Ollama configuration and the
private $frameArgs array, then use an approved local clip and
private human scene annotations. The following four-scene cuts are
**illustrative**, not a claim about the previously verified clip.

~~~powershell
git switch main
git pull --ff-only origin main

$fourArgs = @($frameArgs)
$index = [Array]::IndexOf($fourArgs, "--max-frames")
if ($index -lt 0) { throw "No explicit frame-count option." }
$fourArgs[$index + 1] = "4"

$sceneCutsMs = "2000,4000,6000"
$preview = dotnet @fourArgs --scene-boundaries-ms $sceneCutsMs
$previewExit = $LASTEXITCODE
$preview
"Exit code: $previewExit"
if ($previewExit -ne 0) { throw "Four-scene preflight refused." }
~~~

Expected: dryRun=true, admitted=true, maximumFrames=4,
reasonCode=preflight-only and samplingMode=annotated-scene.
The preflight must not start a media process, call Ollama/Azure,
enqueue a job, hydrate OneDrive or create a published batch.

A later **independently authorized** real frame run is optional and
may disclose up to four private frames. It uses the exact same
command with --apply, --acknowledge-isolated-v38-rehearsal and
--acknowledge-frame-disclosure added. Do **not** paste frame
job UUIDs, private cuts, media locations or captions into GitHub.

After a successful real run, verify a four-row current-complete
batch using the existing video frames audit command. Check the
private scene coverage, expiration/disarm and owned scratch
cleanup. Use the separately approved human-judged retrieval,
multi-clip acceptance and CPU/GPU/cost procedures as needed.

## Automated verification and remaining acceptance

The CLI tests verify four is explicitly accepted, five is refused,\nnew exact-count interval keys are deterministic and distinct,
four-scene preflight is read-only and impossible cuts are rejected
without any jobs. Synthetic PostgreSQL worker integration tests
prove a complete four-scene published batch with four durable
rows, while a three-of-four batch cannot publish.

No actual private media, FFmpeg, Ollama, Azure or Windows process
interruption is used by these new CI tests. **No new local run is
required to review the PR**. A consolidated real four-scene
quality/cost and interruption study is still needed before
acceptance. WI-0190 remains in_progress, **0/4 full criteria**.
