# PostgreSQL local runtime

PostgreSQL is the selected long-term authoritative catalogue under ADR-0009. During WI-0097 and the later migration slices, the existing SQLite catalogue remains authoritative and must not be deleted or replaced.

This runbook establishes the local PostgreSQL service used for migration development and verification on a Windows machine with WSL2 and Podman Desktop.

## Private configuration

The repository contains a safe template only:

    deploy/postgres/.env.example

Create the private environment file:

~~~powershell
Copy-Item .\deploy\postgres\.env.example .\deploy\postgres\.env
~~~

Edit deploy/postgres/.env and replace the password placeholder. The repository-wide .gitignore excludes .env files.

The compose definition uses PostgreSQL 18 and publishes the database port through the Podman machine. On Windows, Podman forwards published ports to Windows localhost. Do not add an explicit `127.0.0.1:` host address to the Compose port mapping: with the WSL machine provider that can bind only the Podman VM loopback and make Windows `127.0.0.1` refuse the connection even though PostgreSQL is healthy inside the container. PostgreSQL data is stored in the named photoidentity-postgres-data volume mounted at /var/lib/postgresql, which is the PostgreSQL 18+ official-image volume boundary.

## Supported Podman baseline on Windows/WSL

As of 2026-09-02, Podman 6.0.x has an open, triaged Windows/WSL localhost port-forwarding regression where container ports are published in the Podman machine but do not carry traffic correctly through Windows localhost. This is tracked upstream as Podman issue #29377 and Microsoft WSL issue #41204.

For WI-0097 Windows/WSL verification, the known-good fallback baseline is **Podman 5.8.5**. Podman Desktop 1.28.3 shipped Podman 5.8.5. Do not change PostgreSQL, Npgsql or Photo Identity catalogue code to compensate for the Podman 6.0.x forwarding regression. The verifier reports the Podman client/server versions and classifies the known failure signature explicitly.

Podman WSL user-mode networking can still be useful for VPN/network compatibility, but maintainer verification showed that enabling it does not resolve this Podman 6.0.x localhost-forwarding regression.

## Start and verify

From the repository root:

~~~powershell
./verify-postgres.ps1
~~~

The Windows application requires a stable localhost endpoint for PostgreSQL. The local Podman runtime does not configure PostgreSQL TLS or GSS encryption, so the supported local connection string explicitly disables both Npgsql negotiation modes. Npgsql defaults both SSL and GSS encryption to `Prefer`; explicit disable avoids a Windows/WSL relay timeout during the optional negotiation round trip while keeping this development/runtime boundary limited to the local machine. WSL normally forwards Linux-bound ports to Windows localhost. If the container is healthy but localhost is unreachable, the verifier now reports the active WSL networking mode, relevant `.wslconfig` settings and whether the Podman-machine IP itself is reachable. It does not silently use the machine IP because that address can change after WSL restart. For Photo Identity, a failing mirrored-networking setup should be changed to WSL NAT with `localhostForwarding=true`, followed by `wsl --shutdown` and a Podman-machine restart.

The verification script:

1. starts deploy/postgres/compose.yaml with Podman Compose;
2. waits for pg_isready inside the container;
3. performs an authenticated `SELECT 1` inside the container, because pg_isready does not prove that the configured password matches the persisted database;
4. confirms Windows can open the published `127.0.0.1:<port>` TCP endpoint;
5. sends a minimal PostgreSQL startup packet from Windows and requires a PostgreSQL protocol response;
6. runs the Npgsql migration test through Windows localhost;
7. if localhost fails, probes the Podman-machine address for diagnosis only and distinguishes a WSL relay failure from a PostgreSQL failure; and
8. creates/drops an isolated disposable PostgreSQL test database while applying the migration bootstrap twice to prove it is versioned and idempotent.

The script does not print the configured PostgreSQL password or connection string.

## Connect Photo Identity to the migration foundation

Supply the PostgreSQL connection string outside source control. For a development shell:

~~~powershell
$env:PhotoIdentity__Postgres__ConnectionString = "Host=127.0.0.1;Port=5432;Database=photoidentity;Username=photoidentity;Password=<private-password>;SSL Mode=Disable;GSS Encryption Mode=Disable"
~~~

Start Photo Identity normally. SQLite remains the active catalogue in WI-0097; PostgreSQL is initialized only as the migration target.

The existing /health endpoint reports both boundaries. A configured and initialized PostgreSQL service appears as:

~~~json
{
  "status": "ok",
  "catalogueProvider": "sqlite",
  "postgres": {
    "configured": true,
    "status": "ready",
    "schemaVersion": 1
  }
}
~~~

PostgreSQL status values are:

- not_configured — no PostgreSQL connection string was supplied;
- ready — connection and migration bootstrap succeeded;
- unavailable — the server could not be reached/opened;
- authentication_failed — PostgreSQL rejected authentication; and
- migration_failed — the server connection succeeded but schema initialization failed.

The health payload never returns the connection string, username, password or server exception text.

## Stop and restart without deleting data

From deploy/postgres:

~~~powershell
podman compose stop
podman compose up -d
~~~

The named volume remains intact across ordinary container stop/start and recreation.

## Destructive development reset

Only use this against a disposable development database. Never use it once PostgreSQL contains the accepted migrated catalogue:

~~~powershell
Push-Location .\deploy\postgres
podman compose down -v
Pop-Location
~~~

This removes the named PostgreSQL data volume.

## Current migration boundary

WI-0097 does not move any source, asset, face, review or archive state out of SQLite. The PostgreSQL schema contains only the migration-history foundation. Foundational catalogue tables are introduced by WI-0098, and the real SQLite-to-PostgreSQL cutover is deferred to WI-0102.


If the server works directly through the Podman-machine address but the Windows localhost relay corrupts or closes PostgreSQL sessions, do not configure Photo Identity with the dynamic machine IP. For a WSL-backed Podman machine whose `UserModeNetworking` value is false, the supported remediation is:

~~~powershell
podman machine stop
podman machine set --user-mode-networking=true
podman machine start
~~~

Then rerun `./verify-postgres.ps1`. Podman documents user-mode networking as the Windows/WSL option that relays guest traffic through a host-side user-space process; the WSL backend otherwise defaults to the standard WSL network path. Because WSL shares its kernel/networking across distributions, enabling this setting while the Podman machine is running can also affect other active WSL distributions; stop the Podman machine to restore the original WSL networking path.
