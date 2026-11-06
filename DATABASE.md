# PostgreSQL database

The application now uses PostgreSQL. Configure the connection string as
`ConnectionStrings:DefaultConnection` or set the
`ConnectionStrings__DefaultConnection` environment variable. For example:

```text
Host=localhost;Port=5432;Database=notekeeping;Username=notekeeping;Password=<your-password>
```

The application applies its EF Core migrations at startup. Ensure the PostgreSQL
database and login exist before starting the app.

## Import existing SQLite data

Back up the SQLite database before importing. Set the PostgreSQL connection
string, then run the importer once against the existing file:

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=notekeeping;Username=notekeeping;Password=<your-password>"
dotnet run -- --import-sqlite "App_Data\NoteKeeping.db"
```

The import command applies PostgreSQL migrations, then copies users, notebooks,
and notes in a transaction while preserving their IDs. It refuses to import
unless the PostgreSQL tables are empty. The SQLite file is opened read-only and
is not modified. Once the import succeeds, start the app normally with the same
connection string.

## Journal data encryption

Note text and notebook titles are encrypted with ASP.NET Core Data Protection
before EF Core writes them to PostgreSQL. Existing plaintext notes and titles
are encrypted at startup. The note and title purpose strings and the
`NoteKeeping` application name must remain stable for existing data to remain
decryptable.

In production, configure `DataProtection__Azure__BlobUri` with an HTTPS URI to
a private Azure Blob that stores the key ring. The app requires this setting in
Production instead of silently using an instance-local directory. Enable a
system-assigned or user-assigned managed identity for the App Service and grant
it the **Storage Blob Data Contributor** role scoped to the key container. Set
`DataProtection__Azure__ManagedIdentityClientId` only when using a
user-assigned identity. Keep Azure Storage's encryption at rest enabled (the
default) and limit all other identities' access to the key blob.

For local development, the key ring remains in `App_Data\DataProtection-Keys`
by default. Set `DataProtection:KeysPath` or `DataProtection__KeysPath` to use
another directory. Do not commit these files or place them in a publicly served
directory, and restrict local filesystem access to the application account.

## Azure key-ring migration, backup, and recovery

Before switching an existing deployment to Blob Storage, migrate every existing
Data Protection key into the configured blob. The Azure Blob provider stores a
single XML `<repository>` containing the key entries; do not start against an
empty blob, or existing encrypted notes and titles will become unreadable.
Perform the migration from a restricted workstation over HTTPS, and do not
leave temporary key-ring exports behind.

The migration and validation commands are explicit and do not change the
Production key-store selection. Sign in locally with `az login` using an
identity that has **Storage Blob Data Contributor** access to the target
container. Keep the Azure App Service `DataProtection__Azure__BlobUri` setting
unset until both commands succeed. Run from the repository root:

```powershell
dotnet run -- --migrate-data-protection-key-ring "https://<account>.blob.core.windows.net/<container>/<blob>"
```

The migration reads every top-level `*.xml` key file from
`App_Data\DataProtection-Keys` (or `DataProtection:KeysPath`), validates the
key documents and unique IDs, and writes a single provider-compatible
`<repository>` XML document. It creates the blob only if it does not exist.
If the blob already exists, the command makes no changes and succeeds only if
every local key ID and full XML key element matches; extra Azure keys are left
untouched. It then reads back and validates a newly-created repository. It
never deletes or changes the local key directory.

Set `ConnectionStrings__DefaultConnection` locally to the Neon connection
string using a secure environment-variable or secret mechanism, then run:

```powershell
dotnet run -- --validate-data-protection-key-ring "https://<account>.blob.core.windows.net/<container>/<blob>"
```

Validation uses the Azure key repository, `NoteKeeping`, and the existing note
and title purposes to read all notebook titles and note texts from PostgreSQL.
It reports counts only, does not print plaintext, and does not write database
values. It fails if it cannot decrypt rows or does not find encrypted values
for both titles and notes.

Only after migration and validation succeed should you set the App Service
`DataProtection__Azure__BlobUri` to the exact same blob URI and restart the
Production app. Preserve a restricted backup of the original key directory and
leave it untouched for rollback.

Enable Azure Blob soft delete and blob versioning for the key container, and
include its key blob in backups. Restrict restore and delete permissions. Test
restoring the database and the corresponding key blob together in a non-
production environment before relying on recovery. Key blobs contain sensitive
key material even though Azure Storage encrypts data at rest.
