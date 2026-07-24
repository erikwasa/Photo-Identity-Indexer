---
id: WI-0002
title: Create solution skeleton
milestone: M00
status_source: ../status/work-items.yaml
depends_on: []
affected_modules: [repository]
---

# WI-0002: Create solution skeleton

## Objective

Create the .NET 10 solution, planned projects, central package management, nullable configuration, editor settings and PowerShell build/test scripts.

## Acceptance criteria

- [x] `dotnet build` succeeds.
- [x] `dotnet test` succeeds.
- [x] Project references enforce the intended dependency direction.
- [x] No model binaries or personal data are committed.

## Implemented structure

- .NET 10 solution with the planned source and test projects
- central build and package configuration
- compact `.slnx` solution file
- PowerShell build and test entry points
- Windows GitHub Actions build
- privacy-focused ignore rules

## Verification

Pull request [#2](https://github.com/erikwasa/Photo-Identity-Indexer/pull/2) contains the implementation.

GitHub Actions run [30129132466](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30129132466) successfully restored, built and tested the solution on Windows with .NET 10.
