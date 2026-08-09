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

- [ ] A repeatable build produces a Windows operator package with a clear executable entry point.
- [ ] Normal use does not require running dotnet publish or setting shell environment variables manually.
- [ ] Application upgrades preserve catalogue/configuration/private derived data outside the package directory.
- [ ] Duplicate-instance and startup-health behavior remains predictable.
- [ ] Package/runtime trade-offs and update procedure are documented and verified on Windows.

## Verification requirements

Automated packaging smoke verification where possible plus human install/copy/start/upgrade verification on Windows using non-destructive catalogue configuration.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
