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
