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

The compose definition uses PostgreSQL 18 and binds the database port to loopback only. PostgreSQL data is stored in the named photoidentity-postgres-data volume mounted at /var/lib/postgresql, which is the PostgreSQL 18+ official-image volume boundary.

## Start and verify

From the repository root:

~~~powershell
./verify-postgres.ps1
~~~

The verification script:

1. starts deploy/postgres/compose.yaml with Podman Compose;
2. waits for pg_isready;
3. creates an isolated disposable PostgreSQL test database;
4. runs the WI-0097 migration bootstrap twice to prove it is versioned and idempotent; and
5. drops the disposable database while leaving the development PostgreSQL service and persistent volume intact.

The script does not print the configured PostgreSQL password or connection string.

## Connect Photo Identity to the migration foundation

Supply the PostgreSQL connection string outside source control. For a development shell:

~~~powershell
$env:PhotoIdentity__Postgres__ConnectionString = "Host=127.0.0.1;Port=5432;Database=photoidentity;Username=photoidentity;Password=<private-password>"
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
