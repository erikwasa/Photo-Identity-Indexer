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

- [ ] A Windows user can start the normal application by double-clicking one file.
- [ ] The launcher opens the browser only after the application is reachable or gives an actionable failure.
- [ ] Repeated launch attempts do not create conflicting server instances.
- [ ] Routine startup no longer requires manually setting the database environment variable in a shell.
- [ ] Existing command-line startup remains available for development/diagnostics.

## Verification requirements

Human verification from Windows Explorer against a clean published output plus automated/script-level checks where practical.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work: packaged/self-contained Windows distribution
- Commands run:
