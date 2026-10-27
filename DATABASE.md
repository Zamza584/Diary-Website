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
are encrypted at startup. Data Protection keys are stored in
`App_Data\DataProtection-Keys` by default; set
`DataProtection:KeysPath` or `DataProtection__KeysPath` to use another
persistent, access-restricted directory.

Keep this key directory backed up and available to every application instance.
Losing the keys makes encrypted journal data unreadable, and instances that use
different key rings cannot decrypt each other's data. Do not commit the keys or
place them in a publicly served directory.
