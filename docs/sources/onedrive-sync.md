# OneDrive synchronised source

The primary photo source is the local directory managed by the official Windows OneDrive sync client. Authentication to the personal Microsoft account remains entirely inside that client.

Benefits:

- No app registration or Graph permissions
- No OAuth implementation
- No enterprise Azure tenant dependency
- No personal OneDrive credentials in the application
- Existing year/month folders remain visible

The source adapter must treat filesystem paths as mutable metadata, not permanent identity. It initially combines source root, relative path, size, last-write time and optional content fingerprints. Moves can later be reconciled by hash.

A direct Microsoft Graph connector is not part of the core plan. It may be added only if an independently permitted registration mechanism becomes available, and the rest of the system must not depend on it.

## Hydration and staging

OneDrive Files On-Demand can expose placeholders whose content is not local. Discovery must therefore be separate from content availability.

The source adapter distinguishes local files, online-only placeholders, files being downloaded, offline failures and sync errors.

Hydration modes:

1. **User-managed hydration:** the user chooses **Always keep on this device** for the evaluation folder. This is the first implementation.
2. **Application-triggered hydration:** opening or copying a placeholder causes OneDrive to retrieve it; the application waits for a readable, complete file.
3. **Staging copy:** hydrated content is copied to a dedicated staging directory before local or Azure processing.

Staging protects against de-hydration, file locks, moves, source changes and interrupted bundle creation. Staged files are verified by size and hash and deleted only after processing or result import is verified.
