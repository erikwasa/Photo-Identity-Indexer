# Windows operator package

This runbook defines the M18 Windows package boundary for normal Photo Identity use. The package is a replaceable code bundle; the catalogue, launcher configuration, analysis output, review proxies, logs and other private derived data stay outside the package directory.

## Supported package

The supported operator package is currently:

- runtime identifier: `win-x64`;
- deployment mode: **self-contained**;
- UI/runtime shape: the existing local ASP.NET Core host plus Blazor WebAssembly application;
- operator entry point: `PhotoIdentity.cmd`;
- local listener: loopback HTTP only by default, normally `http://127.0.0.1:5080`;
- optional phone listener: explicit trusted-LAN HTTPS on one configured non-loopback IP address.

Self-contained deployment is intentional. It makes the ZIP larger and requires rebuilding/replacing the package when the bundled .NET runtime must be updated, but normal use does not depend on a separately installed .NET runtime or on an operator running `dotnet publish`.

MSI/MSIX packaging is not required for this milestone. A ZIP/folder package keeps the installation boundary transparent, supports side-by-side replacement, and avoids introducing installer state while there is only one supported Windows architecture.

## Build a package

From the repository root:

```powershell
./Package-PhotoIdentity.ps1 -Configuration Release
```

The default outputs are:

```text
.artifacts\packages\PhotoIdentity-win-x64\
.artifacts\packages\PhotoIdentity-win-x64.zip
```

The packager:

1. publishes `src/PhotoIdentity.Api` self-contained for `win-x64`;
2. places application binaries under `app`;
3. adds `PhotoIdentity.cmd`, `Start-PhotoIdentity.ps1`, `README.txt`, a safe launcher-configuration example and `package-manifest.json`;
4. rejects an accidental real `PhotoIdentity.launcher.json` or SQLite database in the package; and
5. creates the ZIP and prints compressed/uncompressed package size.

The CI `package-verification` job runs the same package path on Windows and uploads the resulting ZIP as a short-lived workflow artifact.

## Install and start

Extract the complete ZIP to a local folder, for example:

```text
C:\Apps\PhotoIdentity-<version>
```

Then double-click:

```text
PhotoIdentity.cmd
```

`PhotoIdentity.cmd` pins the executable code location to the package's own `app` directory, then delegates startup-health, duplicate-instance and browser behavior to the WI-0051 launcher. This code-path override is deliberately separate from persistent operator settings.

## Durable data and settings

By default, Photo Identity uses:

```text
%LOCALAPPDATA%\PhotoIdentity
```

for local durable application state such as the default catalogue, archive analysis output and launcher logs. Private installations may configure other local non-OneDrive paths where the existing operating policy requires them.

Optional launcher configuration belongs at:

```text
%LOCALAPPDATA%\PhotoIdentity\launcher.json
```

Copy the package's `PhotoIdentity.launcher.example.json` there when configuration is required. For packaged use, normally **do not set `publishPath`**. `PhotoIdentity.cmd` always runs the `app` directory beside the package launcher, while URL and whitelisted `PhotoIdentity__...` settings remain durable outside the package.

### Trusted-LAN phone access

Phone access is **off by default**. Photo Identity is still unauthenticated, so enabling this listener deliberately increases who can reach the application. Use it only on a trusted private network, with a narrowly scoped Windows Firewall rule, and never publish or port-forward it to the internet.

Keep the normal `url` setting as loopback HTTP. Enable mobile access through a separate launcher block:

```json
{
  "url": "http://127.0.0.1:5080",
  "mobileAccess": {
    "enabled": true,
    "listenUrl": "https://<THIS-PC-LAN-IP>:5443",
    "phoneUrl": "https://<CERTIFICATE-HOSTNAME-OR-LAN-IP>:5443",
    "certificatePath": "C:\\PhotoIdentity\\private\\photoidentity-lan.pfx",
    "certificatePasswordEnvironmentVariable": "PHOTOIDENTITY_MOBILE_CERT_PASSWORD"
  }
}
```

The contract is intentionally narrow:

- `listenUrl` must be HTTPS and must use one specific non-loopback IP address. Hostnames and wildcard addresses such as `0.0.0.0`/all interfaces are rejected. If the certificate uses a DNS name, put that name in `phoneUrl` while keeping `listenUrl` on the exact local IP.
- `phoneUrl` is optional and defaults to `listenUrl`; when present it must be an HTTPS origin on the same port.
- `certificatePath` points to an operator-owned PFX outside the replaceable package. Relative certificate paths are resolved relative to `launcher.json`.
- If the PFX has a password, configure only the **name** of a process environment variable with `certificatePasswordEnvironmentVariable`. Set the secret value in that environment before starting Photo Identity. The password is not accepted as launcher JSON and is never printed by the launcher.
- Missing certificates, insecure URLs and wildcard/loopback mobile bindings fail before the application starts. The launcher never falls back to remote HTTP.

The certificate must be valid for the host used in `phoneUrl`, and its issuing CA (or the certificate itself for an intentionally private self-signed setup) must be trusted by the phone. After starting Photo Identity, open `phoneUrl` from the phone on the trusted LAN and confirm all of the following before child use:

1. the browser shows a valid HTTPS connection with no certificate warning;
2. the Photo Identity shell and normal images/API requests load without mixed-content errors;
3. in the browser console or a temporary diagnostic page, `window.isSecureContext === true`;
4. the firewall rule applies only to the intended trusted/private network and HTTPS port.

The launcher continues to use the loopback URL for its own health checks, duplicate-instance detection and local browser launch. Therefore enabling mobile access does not change normal desktop startup semantics. Stop/restart the running Photo Identity process after changing any mobile listener setting.

### Automatic GeoNames timing

The packaged launcher accepts these automatic-enrichment settings:

```text
PhotoIdentity__GeoNames__AutomaticEnrichmentEnabled
PhotoIdentity__GeoNames__AutomaticMinimumRequestIntervalMilliseconds
PhotoIdentity__GeoNames__AutomaticIdlePollIntervalMilliseconds
```

Their startup semantics are:

- `PhotoIdentity__GeoNames__AutomaticEnrichmentEnabled`: `true` or `false`; default `true`. A GeoNames username must still be configured before the automatic worker can make provider requests.
- `PhotoIdentity__GeoNames__AutomaticMinimumRequestIntervalMilliseconds`: milliseconds between normal automatic provider requests; default and minimum `30000` (30 seconds). The launcher rejects values below `30000` with a clear configuration error; it does not silently claim a faster value was applied while clamping it.
- `PhotoIdentity__GeoNames__AutomaticIdlePollIntervalMilliseconds`: milliseconds between checks when no immediately eligible GPS work exists; default `5000`; supported range `1000` through `600000` (1 second through 10 minutes).

These are process-startup settings. Edit `launcher.json`, restart Photo Identity, and inspect **Settings → GeoNames place enrichment** to confirm the effective timing. The `/api/place-enrichment/status` diagnostics also expose both effective automatic intervals. GeoNames quota, account and transport backoff remains authoritative and may delay a retry longer than the configured normal request interval.

Do not place any of these inside the extracted package directory:

- the canonical SQLite catalogue;
- archive-analysis output;
- review proxies;
- private photos or crops;
- real launcher configuration; or
- backups.

## Upgrade or replace the package

Use side-by-side replacement rather than overwriting a running application folder:

1. extract the new ZIP to a new local folder beside the current package;
2. stop the currently running `PhotoIdentity.Api.exe` process;
3. start `PhotoIdentity.cmd` from the new folder;
4. confirm the existing catalogue, settings and expected Review/Library state are present; and
5. delete the old package folder only after the new package is verified.

No catalogue migration or private-data copy is part of a normal package replacement because durable state is outside both package folders. Normal database schema migration remains the application's existing startup responsibility and must continue to follow the SQLite backup/restore policy for risky maintenance.

## Verification

Automated package verification is:

```powershell
./verify-package.ps1 -Configuration Release
```

It uses disposable local application data and a dedicated loopback port. The verification:

- builds the self-contained `win-x64` ZIP;
- extracts and starts package v1 through `PhotoIdentity.cmd`;
- waits for `/health` and verifies a repeated launch reuses the same process;
- confirms the catalogue is created outside the package directory;
- stops v1;
- extracts the same package into a second install directory;
- starts v2 against the same external configuration/catalogue; and
- proves the external configuration and a preservation marker survive the replacement.

Launcher verification additionally exercises the automatic GeoNames timing allow-list with non-default effective values, proves that a below-safe-floor request is rejected before the server starts, preserves the loopback-only primary URL contract, and rejects insecure/wildcard/missing-certificate mobile configurations before startup. Because the launcher and packaged configuration example are deployment-surface inputs, changes to these settings also run the Windows package-verification lane in CI.

M18 completion still requires a human Windows pass: extract/copy the package, double-click `PhotoIdentity.cmd`, inspect Review/Library/Settings on desktop and narrow layout, and perform one non-destructive side-by-side replacement using the maintained catalogue configuration.
