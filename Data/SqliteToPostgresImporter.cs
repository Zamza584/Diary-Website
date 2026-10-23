using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NoteKeeping.Models;

namespace NoteKeeping.Data;

public static class SqliteToPostgresImporter
{
    public static async Task ImportAsync(NoteKeepingDbContext destination, string sqlitePath)
    {
        var fullPath = Path.GetFullPath(sqlitePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The SQLite database to import was not found.", fullPath);
        }

        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await source.OpenAsync();

        var users = await ReadUsersAsync(source);
        var notebooks = await ReadNotebooksAsync(source);
        var notes = await ReadNotesAsync(source);

        await using var transaction = await destination.Database.BeginTransactionAsync();
        if (await destination.UserAccounts.AnyAsync()
            || await destination.Notebooks.AnyAsync()
            || await destination.Notes.AnyAsync())
        {
            throw new InvalidOperationException(
                "SQLite import requires an empty PostgreSQL database. No records were imported.");
        }

        destination.UserAccounts.AddRange(users);
        await destination.SaveChangesAsync();

        destination.Notebooks.AddRange(notebooks);
        await destination.SaveChangesAsync();

        destination.Notes.AddRange(notes);
        await destination.SaveChangesAsync();

        await ResetSequenceAsync(destination, "UserAccounts");
        await ResetSequenceAsync(destination, "Notebooks");
        await ResetSequenceAsync(destination, "Notes");

        await transaction.CommitAsync();
    }

    private static async Task<List<UserAccount>> ReadUsersAsync(SqliteConnection source)
    {
        var users = new List<UserAccount>();
        await using var command = source.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Email, PasswordHash, CreatedAt
            FROM UserAccounts
            ORDER BY Id;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new UserAccount
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Email = reader.GetString(2),
                PasswordHash = reader.GetString(3),
                CreatedAt = DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)
            });
        }

        return users;
    }

    private static async Task<List<Notebook>> ReadNotebooksAsync(SqliteConnection source)
    {
        var notebooks = new List<Notebook>();
        await using var command = source.CreateCommand();
        command.CommandText = """
            SELECT Id, UserAccountId, Title, Date, CurrentPage, TotalPages
            FROM Notebooks
            ORDER BY Id;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            notebooks.Add(new Notebook
            {
                Id = reader.GetInt32(0),
                UserAccountId = reader.GetInt32(1),
                Title = reader.GetString(2),
                Date = reader.GetDateTime(3),
                CurrentPage = reader.GetInt32(4),
                TotalPages = reader.GetInt32(5)
            });
        }

        return notebooks;
    }

    private static async Task<List<Note>> ReadNotesAsync(SqliteConnection source)
    {
        var notes = new List<Note>();
        await using var command = source.CreateCommand();
        command.CommandText = """
            SELECT Id, Text, Date, NotebookId
            FROM Notes
            ORDER BY Id;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            notes.Add(new Note
            {
                Id = reader.GetInt32(0),
                Text = reader.GetString(1),
                Date = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                NotebookId = reader.GetInt32(3)
            });
        }

        return notes;
    }

    private static Task<int> ResetSequenceAsync(NoteKeepingDbContext destination, string tableName)
    {
        var sql = tableName switch
        {
            "UserAccounts" => """
                SELECT setval(pg_get_serial_sequence('"UserAccounts"', 'Id'),
                    COALESCE(MAX("Id"), 1), MAX("Id") IS NOT NULL)
                FROM "UserAccounts";
                """,
            "Notebooks" => """
                SELECT setval(pg_get_serial_sequence('"Notebooks"', 'Id'),
                    COALESCE(MAX("Id"), 1), MAX("Id") IS NOT NULL)
                FROM "Notebooks";
                """,
            "Notes" => """
                SELECT setval(pg_get_serial_sequence('"Notes"', 'Id'),
                    COALESCE(MAX("Id"), 1), MAX("Id") IS NOT NULL)
                FROM "Notes";
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(tableName), tableName, "Unknown identity table.")
        };

        return destination.Database.ExecuteSqlRawAsync(sql);
    }
}
