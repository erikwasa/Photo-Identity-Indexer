Photo Identity for Windows (win-x64)
======================================

START
-----
1. Extract the complete package to a local folder, for example C:\Apps\PhotoIdentity-2026-08-11.
2. Double-click PhotoIdentity.cmd.
3. The launcher starts the packaged local server, waits for /health, and opens the browser.

The package is self-contained and does not require a separately installed .NET runtime. The governed CenterFace and SFace ONNX files required for normal archive advancement are installed into app\models\files during packaging and travel with replaceable application code; a separate source checkout is not required at runtime.

DURABLE DATA AND CONFIGURATION
------------------------------
The package directory is replaceable application code. Do not store the catalogue, analysis output, review proxies, or private configuration inside it.

Default durable application data is under:
  %LOCALAPPDATA%\PhotoIdentity

Optional launcher configuration is read from:
  %LOCALAPPDATA%\PhotoIdentity\launcher.json

Copy PhotoIdentity.launcher.example.json there if you need non-default durable paths. For the packaged application, normally leave publishPath unset; PhotoIdentity.cmd always starts the app directory shipped beside it.

The current launcher example selects the measured review-proxy profile used for the maintained archive:
  PhotoIdentity__ReviewProxyProfileId = jpeg-1600-q78
  PhotoIdentity__ReviewProxyMaximumLongEdge = 1600
  PhotoIdentity__ReviewProxyJpegQuality = 78

High-resolution human review and automatic durable proxy generation require the review-proxy root plus those exact registered profile settings:
  PhotoIdentity__ReviewProxyRoot
  PhotoIdentity__ReviewProxyProfileId
  PhotoIdentity__ReviewProxyMaximumLongEdge
  PhotoIdentity__ReviewProxyJpegQuality

If ReviewProxyRoot is set but ReviewProxyProfileId is missing, existing durable proxies are not selected for normal serving and face review can fall back to the legacy recognition crop. Recognition itself still uses the independent 112x112 SFace model input; that model input is not a human-review image source.

The current launcher example also selects a conservative bounded archive-hydration policy:
  PhotoIdentity__ArchiveHydration__MinimumFreeSpaceReserveBytes = 21474836480  (20 GiB)
  PhotoIdentity__ArchiveHydration__MaximumManagedHydrationBytes = 10737418240 (10 GiB)
  PhotoIdentity__ArchiveHydration__MaximumConcurrentOperations = 2

The reserve is a floor: Photo Identity refuses a managed hydration that would leave the archive volume below 20 GiB free. The 10 GiB limit applies only to Photo Identity-managed hydrated/downloading originals, not to files that were already local or user-pinned. Change these startup values if the machine's storage constraints require a different policy.

Automatic GeoNames enrichment timing is also configurable in launcher.json:
  PhotoIdentity__GeoNames__AutomaticEnrichmentEnabled = true
  PhotoIdentity__GeoNames__AutomaticMinimumRequestIntervalMilliseconds = 30000
  PhotoIdentity__GeoNames__AutomaticIdlePollIntervalMilliseconds = 5000

The request interval is milliseconds and must be at least 30000 (30 seconds). The launcher rejects lower values instead of silently clamping them. The idle poll interval is milliseconds from 1000 through 600000 (1 second through 10 minutes). Provider quota and transport backoff can still delay requests longer than the normal configured pacing. Restart Photo Identity after changing these startup values. Settings shows the effective automatic timing through the GeoNames status diagnostics.

The Settings page shows the effective hydration values, whether managed hydration is enabled, current Photo Identity-managed usage, remaining managed budget, and current free space on the archive volume. These values are startup configuration; edit launcher.json and restart Photo Identity to apply changes.

TRUSTED-LAN PHONE ACCESS
------------------------
Remote phone access is disabled by default. Photo Identity remains unauthenticated, so only enable this mode on a trusted private network and keep the firewall rule limited to the selected private interface/network.

The normal "url" value must remain a loopback HTTP URL. Mobile access is configured separately:

  "mobileAccess": {
    "enabled": true,
    "listenUrl": "https://<THIS-PC-LAN-IP>:5443",
    "phoneUrl": "https://<CERTIFICATE-HOSTNAME-OR-LAN-IP>:5443",
    "certificatePath": "C:\\PhotoIdentity\\private\\photoidentity-lan.pfx",
    "certificatePasswordEnvironmentVariable": "PHOTOIDENTITY_MOBILE_CERT_PASSWORD"
  }

listenUrl must use one specific non-loopback IP address; wildcard bindings such as 0.0.0.0 and hostnames are rejected so enabling phone access does not silently expose unrelated interfaces. phoneUrl may use the DNS hostname present in the certificate and must use the same port.

Keep the PFX and its password outside the package. If the PFX is password-protected, set the named environment variable before starting PhotoIdentity.cmd. The launcher never accepts or prints a certificate password in launcher.json. The certificate chain (or private issuing CA) must be trusted by the phone. Open phoneUrl on the phone and verify the browser reports a valid HTTPS connection before using slideshow features.

Create a narrowly scoped inbound Windows Firewall rule for the configured HTTPS port on the trusted private network only. Do not expose this listener through a router, public firewall profile or internet-facing forwarding. Restart Photo Identity after changing mobile settings.

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

By default the application listens only on the configured loopback HTTP URL. The optional trusted-LAN listener is HTTPS-only, explicit, and must never be exposed to an untrusted or public network.
