Photo Identity for Windows (win-x64)
======================================

START
-----
1. Extract the complete package to a local folder, for example C:\Apps\PhotoIdentity-2026-08-11.
2. Double-click PhotoIdentity.cmd.
3. The launcher starts the packaged local server, waits for /health, and opens the browser.

The package is self-contained and does not require a separately installed .NET runtime.

DURABLE DATA AND CONFIGURATION
------------------------------
The package directory is replaceable application code. Do not store the catalogue, analysis output, review proxies, or private configuration inside it.

Default durable application data is under:
  %LOCALAPPDATA%\PhotoIdentity

Optional launcher configuration is read from:
  %LOCALAPPDATA%\PhotoIdentity\launcher.json

Copy PhotoIdentity.launcher.example.json there if you need non-default settings. For the packaged application, normally leave publishPath unset; PhotoIdentity.cmd always starts the app directory shipped beside it.

High-resolution human review and automatic durable proxy generation require the review-proxy root plus the exact registered proxy profile settings:
  PhotoIdentity__ReviewProxyRoot
  PhotoIdentity__ReviewProxyProfileId
  PhotoIdentity__ReviewProxyMaximumLongEdge
  PhotoIdentity__ReviewProxyJpegQuality

The launcher example leaves the profile-specific values blank instead of guessing them. If ReviewProxyRoot is set but ReviewProxyProfileId is missing, existing durable proxies are not selected for normal serving and face review can fall back to the legacy recognition crop. Use the exact profile id and encoder settings already accepted/registered for the durable catalogue.

The example launcher file also includes the three bounded archive-hydration keys with blank values. Blank or missing values keep Photo Identity-managed hydration disabled. Replace them only with storage-policy values that have been deliberately accepted for this computer:
  PhotoIdentity__ArchiveHydration__MinimumFreeSpaceReserveBytes
  PhotoIdentity__ArchiveHydration__MaximumManagedHydrationBytes
  PhotoIdentity__ArchiveHydration__MaximumConcurrentOperations

The Settings page shows the effective hydration values and whether managed hydration is enabled. These values are startup configuration; edit launcher.json and restart Photo Identity to apply changes.

UPGRADE / REPLACEMENT
---------------------
1. Extract the new package to a new folder beside the old package.
2. Stop the currently running PhotoIdentity.Api.exe process before switching versions.
3. Double-click PhotoIdentity.cmd in the new folder.
4. Confirm the existing catalogue and settings are present.
5. Delete the old package folder only after the new package has been verified.

Because durable data lives outside the package, no catalogue or private derived-data copy is required during a normal package upgrade.

DIAGNOSTICS
-----------
Launcher logs are written below:
  %LOCALAPPDATA%\PhotoIdentity\launcher-logs

The application listens only on the configured loopback HTTP URL. Do not expose it to an untrusted network.
