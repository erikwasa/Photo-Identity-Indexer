# Approved, pinned FFmpeg and FFprobe on Windows

Photo Identity video workers and operator-only video CLIs deliberately reject
executable files **or ancestor directories** that are symbolic links, WinGet
command aliases or junctions. Get-Command ffmpeg.exe may resolve to a
WinGet-managed shim that launches and hashes successfully but still causes
"Approved video tool is missing or linked" during video preflight. Recomputing
the SHA-256 does not cure this failure.

## Permanent operator setup (once per approved version)

1. Obtain **both tools from the same trusted FFmpeg distribution** and review
   publisher provenance, binary version and licensing. They must be installed
   separately from Photo Identity; do not add FFmpeg binaries to this repo or
   application package. Changing versions requires a new explicit approval.
2. Resolve the **real physical executables**, not WinGet command aliases.
   If the distribution is under a symlink/junction, copy approved files into a
   normal local directory on a fixed disk. Use matching FFmpeg and FFprobe
   from one distribution, not unrelated copies.
3. Pin immutable **regular files** in a private local directory, e.g.

       %LOCALAPPDATA%\PhotoIdentity\Tools\FFmpeg\<approved-ffmpeg-sha256>\ffmpeg.exe
       %LOCALAPPDATA%\PhotoIdentity\Tools\FFmpeg\<approved-ffmpeg-sha256>\ffprobe.exe

   Directory name identifies the reviewed FFmpeg build pair. Never overwrite
   pinned binaries or put them in OneDrive.
4. Compute SHA-256 **after copying** both binaries, independently compare
   with the approved originals, then run the copied FFmpeg and FFprobe with
   -version. For dynamic builds, preserve dependencies or select a trusted
   static build. Hashes identify bytes but do not establish publisher trust.
5. Back up the existing
   %LOCALAPPDATA%\PhotoIdentity\launcher.json. In its existing settings
   object set the following four keys to the new **physical absolute paths**
   and the respective **lowercase 64-character SHA-256** digests. Preserve
   existing launcher settings, PostgreSQL connection variable name and
   mobile-access configuration:

       PhotoIdentity__VideoProcessing__FfmpegPath
       PhotoIdentity__VideoProcessing__FfmpegSha256
       PhotoIdentity__VideoProcessing__FfprobePath
       PhotoIdentity__VideoProcessing__FfprobeSha256

   The packaged app reads these persistent settings across package updates.
   Do not include credentials in the launcher JSON.
6. Validate from the repository root without starting video processing:

~~~powershell
$approved = .\Get-PhotoIdentityApprovedVideoTools.ps1
& $approved.FfmpegPath -version | Select-Object -First 1
& $approved.FfprobePath -version | Select-Object -First 1
.\Start-PhotoIdentity.ps1 -ValidateConfigurationOnly
~~~

The shared helper only **reads** the launcher, checks physical Windows
paths and every ancestor for reparse points, then verifies both hashes.
It does not copy, install, run, rewrite or approve the executables. If the
launcher configuration is in another location, supply -ConfigurationPath.
The launcher also performs configuration validation and the running
application independently checks tool digests and private cache boundaries.

## Mandatory convention for video CLI runbooks

Use the approved persistent launcher configuration, **not** Get-Command,
PATH, freshly calculated ad hoc hashes or old sample absolute paths:

~~~powershell
$approved = .\Get-PhotoIdentityApprovedVideoTools.ps1
$ffmpeg = $approved.FfmpegPath
$ffmpegSha = $approved.FfmpegSha256
$ffmpegHash = $approved.FfmpegSha256
$ffprobe = $approved.FfprobePath
$ffprobeSha = $approved.FfprobeSha256
$ffprobeHash = $approved.FfprobeSha256
~~~

Choose the variable names needed by the particular CLI. The application
rechecks the supplied path/digest before running FFmpeg. Local model assets,
scratch, proxy cache, PostgreSQL and the Azure Ollama bridge remain separate
prerequisites. A passing tool check is **not** authorization to enable video
processing.

## Upgrade and troubleshooting

WinGet updates do not update an existing pinned copy. Review any new
distribution, create a **new** versioned directory, approve both new hashes,
update all four launcher properties together, and rerun the helper and
launcher configuration check. Retain old copies until no active configuration
or job refers to them; never delete the private video cache as part of an
FFmpeg upgrade.

If the CLI says "Approved video tool is missing or linked", inspect the
configured file **and each parent** for the ReparsePoint attribute. Fix the
approved path instead of disabling the guard or repeatedly selecting a
WinGet alias. A digest mismatch is different: check the distribution and
explicitly reapprove, not blindly replace the recorded digest. If the
digest is valid but a copied executable cannot run, verify required DLLs,
static-build compatibility and Windows process permissions.

Pinning tools requires no database migration, video proxy regeneration,
Azure activity or OneDrive hydration.

## WI-0190 checkpoint 7 maintained-host lesson (2026-10-31)

The maintainer encountered the WinGet/reparse-point rejection during the
selected FrameEvidence preflight. A physical approved FFmpeg executable
passed the check, enabling an explicitly authorized real-video two-frame
publication via the operator-controlled Azure Ollama bridge and local CLIP.
The execution guard should remain intact; runbooks should consistently use
persistently pinned physical tools.
