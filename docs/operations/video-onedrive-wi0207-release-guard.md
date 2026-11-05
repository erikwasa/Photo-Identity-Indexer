# WI-0207 — conservative video hydration release guard (checkpoint 2)

**Maintained-host checkpoint 1 accepted:** 16 of 16 synthetic preflight tests passed; CLI exit 0; nine current supported candidates with no playable proxy (two local, seven pinned-local), zero online-only, zero dependent jobs, zero managed records. No original media was read, hydrated or released.

**This checkpoint remains synthetic-only.** The worker previously called OneDrive release at the end of any video attempt that requested hydration, potentially clearing a user pin or interrupting further work. The new release policy checks live pinning and availability, fresh current-revision/exclusion and queued/running dependency evidence, and photo-work priority. Any ambiguous, pinned, stale, downloading, unknown or failed check retains the original. Existing successful proxies are not invalidated.

## PowerShell 7.6 verification after this PR merges

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
if ($LASTEXITCODE -ne 0) { throw "Cannot switch main." }
git pull --ff-only origin main
if ($LASTEXITCODE -ne 0) { throw "Cannot fast-forward main." }

dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoHydrationReleaseGuardTests|FullyQualifiedName~VideoOnlineOnlyPreflightTests"
if ($LASTEXITCODE -ne 0) { throw "WI-0207 release guard tests failed." }

dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw "Documentation validation failed." }
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
if ($LASTEXITCODE -ne 0) { throw "Generated docs drift." }
~~~

These commands do not access private OneDrive originals, run FFmpeg, start Archive or change pinning state. Provide only test totals and documentation PASS/FAIL.

## Important remaining limitation

Photo Identity currently requests an online-only video through the Windows +p Files On-Demand pin operation. A resulting pin bit is indistinguishable from a later explicit user choice to keep the file locally. For safety, this checkpoint **retains every pinned video original**, including those pinned by the application itself. It does not yet claim bounded storage release. WI-0207 still requires durable shared managed-hydration ownership, budget enforcement, safe user-versus-app pin distinctions, crash/restart recovery and a separately approved single real online-only hydration/reuse/release test.

Keep **Settings → Video processing → Include online-only videos OFF**. Do not manually unpin files, force OneDrive downloads, run a video job or recreate an online-only sample just to verify this checkpoint.
