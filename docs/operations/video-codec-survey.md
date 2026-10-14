# WI-0150: private video codec and metadata survey

Use this to close WI-0150's **real-archive evidence gate**. No personal videos or paths should be uploaded to GitHub. Report *aggregated* codec, audio, rotation, duration and tag-presence counts only.

## Prerequisites

- Run locally on the maintained Windows host with PowerShell 7.
- Install an **operator-approved** `ffprobe.exe` locally if available; this does not authorize adding FFmpeg binaries to Photo Identity's runtime/package. Record version, build/configuration, download source and license flags *privately*.
- Locate a representative set of **already locally hydrated** MOV and MP4 files from different capture devices/years. Do not probe OneDrive online-only placeholders: opening them can hydrate large originals.

## Survey

From a checkout, in PowerShell 7:

```powershell
# Change this to your archive's synced folder (do not publish this path).
$archiveRoot = 'C:\Path\To\Your\OneDriveArchive'
$ffprobe = 'C:\Path\To\ffprobe.exe'

# Aggregated JSON is saved under LocalAppData, outside the repository.
.\tools\VideoArchiveCodecSurvey.ps1 -ArchiveRoot $archiveRoot -FfprobePath $ffprobe -SamplesPerExtension 16

# Record version and configuration for the specific executable (private notes).
& $ffprobe -version
& $ffprobe -buildconf
```

The script reads only already locally available videos, skips files marked offline/recall-on-access, samples at most N inputs per extension, imposes a per-probe timeout and writes no paths, names, GPS coordinates or camera timestamps. Its `creation_time_present` and `location_tag_present` fields are **presence booleans**, not values. Treat classification as conservative: some locally hydrated files with ambiguous OneDrive flags may be skipped. A failed probe does not imply a codec is unsupported.

Survey output defaults to `%LOCALAPPDATA%\PhotoIdentity\diagnostics\video-codec-survey.json`. Inspect it privately before sharing aggregated rows. The file deliberately does not contain per-file paths or raw metadata tags, but duration/device/codec combinations may still be sensitive; publishing is optional.

## Record these decisions in WI-0150 review

- Counts for enumerated, locally available and skipped (online-only/uncertain) videos by extension, plus successfully probed versus failed/timeout cases.
- Video codec/profile/pixel-format, audio codec, container, duration bracket, orientation/rotation, variable-frame-rate indicators, HDR/10-bit samples and creation/GPS tag presence.
- Whether representative iPhone MOV/HEVC, MP4/H.264, vertical and longer clips exist and decode successfully on the chosen Windows machine. Include privately held samples for unusual codecs.
- Which actual formats are supported initially versus explicitly reported as unsupported; confirm ffprobe build provenance/license flags and permitted deployment method.
- Maintainer acceptance of [ADR-0012](../decisions/ADR-0012-isolated-video-media-architecture.md), initial resource limits and separate opt-in video discovery before WI-0151 begins.

**No runtime video work or mass transcoding is authorized merely by running this survey.**
