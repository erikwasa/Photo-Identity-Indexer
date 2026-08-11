---
id: WI-0051
title: Add a one-click Windows launcher
milestone: M18
status_source: ../status/work-items.yaml
depends_on: [WI-0041]
related_adrs: []
affected_modules: [repository-root, PhotoIdentity.Api]
---

# WI-0051: Add a one-click Windows launcher

## Objective

Provide a double-clickable Windows entry point that starts the existing published Photo Identity application and opens the local browser without requiring the operator to remember environment variables or command sequences.

## Why

Routine use currently involves several setup/start commands even though the API already hosts the web application. A small launcher can remove most day-to-day startup friction before full packaging is addressed.

## In scope

- Add a Windows launcher file/script intended for normal local operation.
- Load supported saved/bootstrap configuration needed to locate and start the published application.
- Start the API on the intended local endpoint and open the browser when healthy.
- Detect an already-running instance and avoid launching duplicate servers.
- Surface actionable errors for missing publish output, missing configuration or startup failure.
- Keep developer build/test scripts separate from the operator launcher.

## Out of scope

- Producing an installer or fully self-contained distribution.
- Changing the catalogue path from the web settings page.
- Replacing developer CLI tooling.

## Acceptance criteria

- [x] A Windows user can start the normal application by double-clicking one file.
- [x] The launcher opens the browser only after the application is reachable or gives an actionable failure.
- [x] Repeated launch attempts do not create conflicting server instances.
- [x] Routine startup no longer requires manually setting the database environment variable in a shell.
- [x] Existing command-line startup remains available for development/diagnostics.

## Verification requirements

Human verification from Windows Explorer against a clean published output plus automated/script-level checks where practical.

## Completion notes

- Files changed:
  - `Start-PhotoIdentity.cmd` is the double-clickable Windows entry point.
  - `Start-PhotoIdentity.ps1` loads local bootstrap configuration, enforces loopback hosting, detects an existing healthy instance or conflicting port, starts the published API and opens the browser only after `/health` succeeds.
  - `PhotoIdentity.launcher.example.json` documents the supported bootstrap shape without containing private archive paths or accepted hydration-policy values; the real `PhotoIdentity.launcher.json` is ignored by Git.
  - `verify-launcher.ps1` publishes to disposable test output, starts the application twice and asserts that the same single healthy server process remains.
  - `.github/workflows/build.yml` runs the Windows launcher verification on the normal repository gate.
  - `docs/operations/local-operator-guide.md` documents normal launcher setup/use and retains the command-line startup path for diagnostics.
- Trade-offs:
  - WI-0051 remains framework-dependent and starts the existing published host; self-contained packaging, installation and shortcuts belong to WI-0052.
  - The `.cmd` wrapper invokes the repository PowerShell script with process-scoped `ExecutionPolicy Bypass` so a normal double-click is not blocked by a machine-wide script policy; the launcher itself remains plain text and reviewable.
  - The browser application remains restricted to a loopback HTTP URL by the launcher because it is unauthenticated; trusted-network hosting continues to require an explicit developer/diagnostic command.
- Deferred work: packaged/self-contained Windows distribution and installer integration under WI-0052.
- Commands run: validation is delegated to the repository GitHub Actions Windows gate, including the dedicated `verify-launcher.ps1` step; human Windows Explorer verification remains required before completion.
