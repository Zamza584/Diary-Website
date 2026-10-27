using System.Data;
using NoteKeeping.Services;
using Microsoft.EntityFrameworkCore;

namespace NoteKeeping.Data;

public static class NoteTextEncryptionMigration
{
    public static async Task EncryptLegacyNotesAsync(
        NoteKeepingDbContext dbContext,
        NoteTextEncryption noteTextEncryption,
        CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var stateTableCommand = connection.CreateCommand())
            {
                stateTableCommand.Transaction = transaction;
                stateTableCommand.CommandText =
                    """CREATE TABLE IF NOT EXISTS "__NoteKeepingDataMigrations" ("Name" text PRIMARY KEY)""";
                await stateTableCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var stateCheckCommand = connection.CreateCommand())
            {
                stateCheckCommand.Transaction = transaction;
                stateCheckCommand.CommandText =
                    """SELECT EXISTS (SELECT 1 FROM "__NoteKeepingDataMigrations" WHERE "Name" = @name)""";

                var nameParameter = stateCheckCommand.CreateParameter();
                nameParameter.ParameterName = "@name";
                nameParameter.Value = "EncryptNoteTextV1";
                stateCheckCommand.Parameters.Add(nameParameter);

                if ((bool)(await stateCheckCommand.ExecuteScalarAsync(cancellationToken) ?? false))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
            }

            var legacyNotes = new List<(int Id, string Text)>();

            await using (var selectCommand = connection.CreateCommand())
            {
                selectCommand.Transaction = transaction;
                selectCommand.CommandText =
                    """SELECT "Id", "Text" FROM "Notes" WHERE "Text" NOT LIKE 'dp1:%'""";

                await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    legacyNotes.Add((reader.GetInt32(0), reader.GetString(1)));
                }
            }

            await using (var updateCommand = connection.CreateCommand())
            {
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = """UPDATE "Notes" SET "Text" = @text WHERE "Id" = @id""";

                var textParameter = updateCommand.CreateParameter();
                textParameter.ParameterName = "@text";
                updateCommand.Parameters.Add(textParameter);

                var idParameter = updateCommand.CreateParameter();
                idParameter.ParameterName = "@id";
                idParameter.DbType = DbType.Int32;
                updateCommand.Parameters.Add(idParameter);

                foreach (var note in legacyNotes)
                {
                    textParameter.Value = noteTextEncryption.Encrypt(note.Text);
                    idParameter.Value = note.Id;
                    await updateCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await using (var stateInsertCommand = connection.CreateCommand())
            {
                stateInsertCommand.Transaction = transaction;
                stateInsertCommand.CommandText =
                    """INSERT INTO "__NoteKeepingDataMigrations" ("Name") VALUES (@name) ON CONFLICT ("Name") DO NOTHING""";

                var nameParameter = stateInsertCommand.CreateParameter();
                nameParameter.ParameterName = "@name";
                nameParameter.Value = "EncryptNoteTextV1";
                stateInsertCommand.Parameters.Add(nameParameter);

                await stateInsertCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
