---
id: WI-0052
title: Package Photo Identity as a Windows application
milestone: M18
status_source: ../status/work-items.yaml
depends_on: [WI-0048, WI-0051]
related_adrs: []
affected_modules: [repository-root, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0052: Package Photo Identity as a Windows application

## Objective

Turn the launcher/publish workflow into a repeatable Windows application package so routine use does not depend on manually publishing the repository or managing runtime command lines.

## Why

WI-0051 removes startup friction but still assumes a prepared publish output. A packaged application creates a clearer long-term operator boundary and makes the CLI/scripts primarily development and maintenance tools.

## In scope

- Define a repeatable Windows publish/package profile for the supported architecture.
- Provide an application executable/shortcut entry point that starts the local server and opens the hosted UI.
- Persist bootstrap settings in a documented local application-data location outside the repository.
- Handle single-instance/startup-health behavior established by WI-0051.
- Decide and document framework-dependent versus self-contained deployment based on package size and update implications.
- Produce a simple upgrade/replacement workflow that preserves the catalogue and generated private data.

## Out of scope

- Building a general-purpose cross-platform desktop application.
- Requiring an MSI/MSIX installer unless the packaging investigation shows it adds clear value.
- Moving private catalogue/photo data into the application installation directory.

## Acceptance criteria

- [x] A repeatable build produces a Windows operator package with a clear executable entry point.
- [x] Normal use does not require running dotnet publish or setting shell environment variables manually.
- [x] Application upgrades preserve catalogue/configuration/private derived data outside the package directory.
- [x] Duplicate-instance and startup-health behavior remains predictable.
- [x] Package/runtime trade-offs and update procedure are documented and verified on Windows.

## Verification requirements

Automated packaging smoke verification where possible plus human install/copy/start/upgrade verification on Windows using non-destructive catalogue configuration.

## Completion notes

- Files changed:
  - `Package-PhotoIdentity.ps1` builds the repeatable self-contained `win-x64` folder/ZIP package.
  - `packaging/windows/PhotoIdentity.cmd` is the normal packaged entry point and pins only the package code location.
  - `packaging/windows/PhotoIdentity.launcher.example.json` demonstrates durable settings without a package-specific `publishPath`.
  - `packaging/windows/README.txt` documents start, durable-data and side-by-side replacement behavior inside the package.
  - `Start-PhotoIdentity.ps1` accepts an explicit package publish-path override while retaining WI-0051 health/duplicate-instance semantics and private local configuration.
  - `verify-package.ps1` builds/extracts/starts/restarts/replaces a disposable package and verifies external catalogue/configuration preservation.
  - `.github/workflows/build.yml` runs package verification on an isolated Windows runner and uploads the produced ZIP as a short-lived artifact.
  - `docs/operations/windows-package.md` records the package/runtime decision and operator upgrade procedure.
- Trade-offs:
  - `win-x64` self-contained deployment is selected. The ZIP is larger and bundled .NET runtime updates require a rebuilt package, but the operator does not need a separately installed .NET runtime.
  - A transparent ZIP/folder package is preferred over MSI/MSIX for M18. It supports side-by-side replacement without introducing installer state or moving private data into an installation directory.
  - The package entry point remains a Windows command file backed by the reviewed PowerShell launcher rather than duplicating startup/health behavior in a second launcher implementation.
- Deferred work:
  - MSI/MSIX, code signing, auto-update and cross-platform desktop packaging remain outside M18.
- Commands run by CI/package verification:
  - `./Package-PhotoIdentity.ps1 -Configuration Release`
  - `./verify-package.ps1 -Configuration Release -Port 5083`
  - existing build/test/docs/review/launcher gates remain unchanged.
- Maintainer Windows install/copy/start/upgrade verification completed on 2026-08-17; WI-0052 is accepted as complete.
