# WI-0212 — video metadata and manual people: verification

## Current delivery state

This is a **development candidate**, not accepted production functionality.
The code is isolated on the WI-0212 branch and must pass CI before a
maintainer performs real-media checks or starts a production v39 migration.

## Implemented boundary

WI-0151 already records capture time/provenance, make/model, GPS, coded
dimensions, rotation, duration and frame rate when present. WI-0212 reads
those PostgreSQL fields in Video Details; it does **not** add a second
FFprobe pipeline. Missing values display Unknown, not guessed timestamps
from filesystem modification time.

PostgreSQL v39 adds video-only append-only histories for manual capture-time,
canonical Place and known-person presence. Editing requires a current,
included video revision, and source exclusions purge the annotations.
Photo metadata, face evidence, recognition, saved collections and originals
are not modified.

- Manual person tagging means *this person appears in the video somewhere*,
  not a recognized face, timestamped person track or identity suggestion.
- Manual Places/clears take precedence over automatic GeoNames results.
- The explicit "Look up Place from GPS" control reuses the configured
  GeoNames provider and existing photo coordinate cache *reads*. Opening
  details never sends GPS or triggers hydration; video-only GPS is not
  newly added to a global cache. Provider pacing still applies.
- Recording time is displayed in UTC; manual edits require ISO 8601 with
  an explicit UTC designator or time-zone offset, e.g.
  2024-05-06T14:30:00+02:00.
- Retained proxies can still play normally; a missing proxy does not
  prevent catalogue metadata editing or initiate processing.

## Automated checks — development stage

On an updated checkout using Windows PowerShell 7.6:

~~~powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"
dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw "Docs validation failed" }
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
if ($LASTEXITCODE -ne 0) { throw "Generated docs differ" }
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoDetailsPostgresTests|FullyQualifiedName~VideoDetailsLayoutContractTests"
if ($LASTEXITCODE -ne 0) { throw "WI-0212 tests failed" }
~~~

The PostgreSQL test **does not actually execute** unless
PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING is configured.
Use a disposable PostgreSQL admin connection, never photoidentity_main.
The fixture creates and deletes its own temporary test database.
CI build, docs gates, migration and API checks must pass before final
maintainer verification.

## Final maintained-PC acceptance — after development is complete

1. Confirm a current recoverable PostgreSQL backup, then rehearse v39
   upgrade and repeat initialization against an isolated restored DB.
   Do not open the new application against production before backup.
2. Launch the tested build with the existing approved Windows launcher
   and configured PostgreSQL connection.
3. Open a deliberately selected current video from Videos -> Video
   details & people. Inspect date **and time**, make/model, duration,
   rotation-correct resolution and GPS. Some files legitimately lack
   these fields; UI must say Unknown instead of fabricating metadata.
4. On a video with known embedded GPS and configured GeoNames, explicitly
   request Place lookup. Check canonical hierarchy and manual correction
   precedence. A non-GPS video must not contact the provider.
5. Edit recording time with an ISO time-zone offset; verify persistence
   after restart. Clear it and check that the extracted value is restored.
6. Add/remove an existing canonical person. Verify presence history is
   independent of face observations, review actions, suggestions and
   photo collections, including when no face was detected in video.
7. Open another *already catalogued* video whose original is online-only.
   Read/edit details without playback, processing or source access.
   Confirm the original stays online-only and unpinned.
8. Confirm ordinary photo Details/People/Places, photo search, video
   browsing/playback and Video Search regression checks pass.

Share sanitized PASS/FAIL only. Do not upload names, GPS coordinates,
filenames, original paths, PostgreSQL credentials or private frames.

## Deferred

Automatic background video geocoding, bulk rescans, collection membership
(WI-0153), video slideshow (WI-0154), video-face recognition (WI-0155)
and video-level descriptions/search revision (WI-0213) remain separate
future scope. The current manually triggered lookup should not be
confused with the photo background enrichment worker.
