---
id: WI-0032
title: Validate documentation from a clean setup
milestone: M15
status_source: ../status/work-items.yaml
depends_on: [WI-0031]
affected_modules: [docs, verify-local.ps1, verify-review.ps1, tools/PhotoIdentity.Docs]
---

# WI-0032: Validate documentation from a clean setup

## Objective

Prove that the rewritten documentation is complete and comprehensible by following it from a clean Windows setup rather than relying on project memory.

## Acceptance criteria

- [ ] A clean checkout can install models, build, test and run synthetic verification using only the documented steps.
- [ ] The documented local catalogue and review flow works on Windows and Pixel over a trusted network.
- [ ] The 500-image pilot and multi-model comparison procedures identify every required input and expected output.
- [ ] Every command is executed or covered by an automated documentation test where practical.
- [ ] Broken links, stale generated status, unexplained terms and hidden prerequisites are rejected by validation.
- [ ] A second reading pass records confusing sections and resolves them before completion.
- [ ] Azure instructions remain clearly optional and deferred until access is available.

## Validation boundary

This is an independent operator validation, not another implementation pass. Use a fresh checkout and follow the documentation in order. Do not silently fill gaps from project memory. When a command, prerequisite, path or expected result is unclear, record the confusion before resolving it.

Private photos, names, catalogue paths, face identifiers, embeddings, manifests, reports and screenshots remain outside Git. Repository evidence should contain only pass/fail results, privacy-safe aggregate facts and any documentation corrections.

## Phase 1: clean Windows environment

Use Windows 10 or 11 on a local NTFS disk. A new Windows user profile, disposable VM or a separate clean directory is acceptable, provided it does not reuse repository build output, installed repository models or a previous test workspace.

Confirm these prerequisites without consulting old project notes:

- Git;
- .NET 10 SDK;
- PowerShell 7 or Windows PowerShell 5.1;
- enough local disk space for model files, catalogue, crops, published application and reports; and
- a Pixel browser on the same trusted private network for the later device check.

Record:

- Windows version;
- PowerShell edition and version;
- `dotnet --info` summary;
- whether the checkout and workspace were genuinely clean; and
- the commit being validated.

## Phase 2: start-here and automated repository checks

From a new checkout of `main`, begin at `README.md` and follow its link to `docs/operations/local-operator-guide.md`. Do not begin from this work-item page.

Run the documented baseline commands from the repository root:

```powershell
./build.ps1
./test.ps1
./verify-local.ps1 -InstallModels
./verify-review.ps1 -Mode Smoke -Configuration Release
```

Also run the documentation and cross-PowerShell checks explicitly:

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
powershell.exe -NoProfile -File ./Invoke-MultiModelComparison.ps1 -SelfTest
pwsh -NoProfile -File ./Invoke-MultiModelComparison.ps1 -SelfTest
```

Pass criteria:

- Release build succeeds;
- all tests pass;
- pinned model files install and verify against their manifests;
- documentation links and registries validate;
- generated documents are current;
- the disposable published review smoke passes; and
- the multi-model script self-test passes under every installed PowerShell edition.

Record any command that requires an undocumented working directory, environment variable, tool, permission or manual correction.

## Phase 3: isolated catalogue workspace

Create a new workspace outside the repository using the variables from the local operator guide:

```powershell
$root = "C:\PhotoIdentityDocumentationValidation"
$source = Join-Path $root "source"
$output = Join-Path $root "outputs"
$db = Join-Path $root "catalogue.db"
$publish = Join-Path $root "review-app"
$evaluation = Join-Path $root "model-lab"
$backup = Join-Path $root "backups"

New-Item -ItemType Directory -Force `
  -Path $source,$output,$evaluation,$backup | Out-Null
```

Use private test media outside the repository. The full acceptance run should use a representative set of approximately 450–550 images. A smaller disposable set may be used first to identify documentation mistakes before committing time to the full run.

Confirm that:

- source and generated output are separate;
- the SQLite database is on a local disk rather than a network or synchronised folder;
- source photos remain unchanged; and
- no private path or data is added to the repository.

## Phase 4: baseline processing, interruption and resume

Follow the documented `batch start` command with explicit YuNet FP32 and SFace FP32 model IDs. Save the printed run ID.

Then:

1. run `batch status` and confirm the documented counters are present;
2. interrupt processing safely before completion on a disposable or recoverable run;
3. run `batch resume` with the saved run ID;
4. confirm resume uses the persisted model revisions and does not create duplicate canonical source revisions; and
5. complete the representative 450–550-image run with zero unexplained failures.

Record only aggregate counts and pass/fail observations. Do not record filenames, people, source paths or revision identifiers in Git.

## Phase 5: Windows and trusted-network Pixel review

Publish the Release application exactly as documented and start it with `PhotoIdentity__DatabasePath` set to the validation catalogue.

On Windows:

- open `http://localhost:5080`;
- confirm Faces, People, Progress, Audit and Collections load;
- perform an assignment, rejection and undo on disposable test decisions;
- create or select a person;
- regenerate exact-model suggestions and inspect the reported model ID/hash;
- refresh and confirm committed review state persists; and
- verify no source paths, crop paths or embeddings are displayed.

On Pixel, using the same trusted private network:

- use the Windows machine's private LAN address;
- keep any firewall rule restricted to the Private profile and chosen port;
- confirm portrait and landscape layouts have no horizontal page overflow;
- confirm selectors, review actions, pagination or continuous loading and Collections are touch-usable; and
- confirm stale service-worker/site data recovery works when the published client changes.

Do not use a public tunnel, guest network or unauthenticated internet exposure.

## Phase 6: suggestions, evaluation and collections

Follow the operator guide without substituting remembered commands:

1. regenerate suggestions for the exact FP32 embedder revision;
2. confirm canonical assignments and append-only review history remain unchanged;
3. export a reviewed-catalogue evaluation manifest;
4. run evaluation;
5. repeat export and evaluation with unchanged inputs and compare file hashes;
6. browse confirmed-only single-person and multi-person Collections queries; and
7. request a version-1 neutral collection manifest.

Pass criteria include:

- exact detector and embedder provenance is reported;
- deterministic reruns produce identical hashes;
- `any` collection totals are not lower than corresponding `all` totals;
- manifest format is `photoidentity.collection-manifest` and version is `1`; and
- no response exposes source roots, source keys, crop paths or filenames.

## Phase 7: multi-model procedure inspection

The accepted FP32-versus-INT8 comparison has already been completed; WI-0032 does not require another costly full comparison unless a documentation defect makes rerunning it necessary.

Validate the procedure by:

```powershell
powershell.exe -NoProfile -File ./Invoke-MultiModelComparison.ps1 -SelfTest
pwsh -NoProfile -File ./Invoke-MultiModelComparison.ps1 -SelfTest
```

Then read `docs/operations/multi-model-comparison.md` and confirm that a new operator can identify, without project memory:

- configuration fields and example values;
- required source, catalogue and workspace paths;
- model installation and exact revision rules;
- preflight, resume and cleanup behavior;
- expected summary, logs, manifests and reports;
- Windows and Pixel human gates;
- privacy restrictions; and
- the accepted recommendation to retain SFace FP32 as the current default.

Run a small disposable comparison only when necessary to prove a command or prerequisite that the self-test cannot cover.

## Phase 8: backup, restore and recovery reading

With all writers stopped:

- create a stopped-state catalogue backup;
- retain the corresponding crop/artefact directories from the same maintenance window;
- verify SQLite integrity, foreign keys and user version;
- restore into a separate location; and
- confirm the restored catalogue can open in the published application.

Exercise or inspect every common recovery section:

- model unavailable or hash mismatch;
- interrupted batch;
- stale browser assets/service worker;
- SQLite locking; and
- missing images or crops.

A recovery step fails validation when it depends on an unstated path, process state, permission or destructive action.

## Phase 9: second reading pass

After completing the commands, reread:

- `README.md`;
- `docs/operations/local-operator-guide.md`;
- `docs/operations/local-evaluation.md`;
- `docs/operations/multi-model-comparison.md`;
- `docs/operations/sqlite-persistence.md`;
- `docs/glossary.md`; and
- the architecture overview and data-model pages.

Record every confusing, duplicated, contradictory or unexplained section. Correct repository documentation before WI-0032 is completed. Verify that Azure remains clearly optional and deferred; no Azure account or resource is required for M15.

## Evidence to return

Keep detailed local notes outside Git. Send the following privacy-safe summary for the completion update:

```text
Validated commit:
Windows version:
PowerShell versions:
.NET SDK:
Clean checkout/workspace: pass/fail
Build and tests: pass/fail
Model install and verification: pass/fail
Documentation validate/generate check: pass/fail
Synthetic review smoke: pass/fail
PowerShell comparison self-tests: pass/fail
450-550 image processing: pass/fail; aggregate completed/failed counts
Interrupt and resume: pass/fail
Windows review workflow: pass/fail
Pixel browser/device:
Pixel trusted-network workflow: pass/fail
Suggestions and exact-model provenance: pass/fail
Deterministic export/evaluation: pass/fail
Collections and neutral manifest: pass/fail
Stopped-state backup and restore: pass/fail
Second reading pass completed: yes/no
Documentation corrections required:
Remaining uncertainty or failures:
```

WI-0032 and M15 complete only after every acceptance criterion is checked, all required corrections are merged and the privacy-safe verification summary is recorded.