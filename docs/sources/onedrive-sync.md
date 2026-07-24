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
