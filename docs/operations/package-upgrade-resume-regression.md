# Packaged archive resume across application upgrades

## Regression scenario

A durable archive processing run can outlive the replaceable Windows package that created it. The run configuration records absolute repository and model paths so normal same-installation resume is deterministic.

During real Windows verification on 2026-08-13, Photo Identity was confirmed to be running from `C:\PhotoIdentity\v1.3\app\PhotoIdentity.Api.exe`, but Archive advancement attempted to resolve CenterFace from `C:\PhotoIdentity\v1.2\app\models\files\centerface.onnx`. The v1.3 package itself contained the governed model files; the stale path came from the unfinished v1.2 run configuration.

## Required behavior

For bounded Archive advancement, repository/model paths inside a package are replaceable runtime state rather than durable archive identity.

When the current analysis profile matches an unfinished durable run but that run's saved `RepositoryRoot` or `ModelDirectory` differs from the current runtime paths:

1. request cancellation of the stale run;
2. keep previously recorded exact revision/profile completions;
3. allow normal archive advancement to create a current-runtime run for only revisions that remain pending;
4. never copy models into, reopen, or otherwise depend on the previous package directory.

This does not change explicit CLI `archive resume` semantics; the corrective behavior is scoped to the bounded operator application path where package replacement is expected.

## Verification

Automated regression coverage checks that different side-by-side package runtime paths are detected as stale while identical paths remain resumable. The normal integration suite continues to cover durable exact revision/profile completion reuse.

Human Windows verification should:

1. stop the previous packaged process;
2. start the corrective package from a new version directory against the same durable catalogue;
3. confirm the running `PhotoIdentity.Api.exe` path is the new package;
4. run **Advance archive**;
5. confirm no error references a previous package directory;
6. confirm already-analyzed images remain analyzed and pending work continues using the current package models.
