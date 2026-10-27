using System.Data;
using Microsoft.EntityFrameworkCore;
using NoteKeeping.Services;

namespace NoteKeeping.Data;

public static class NotebookTitleEncryptionMigration
{
    public static async Task EncryptLegacyTitlesAsync(
        NoteKeepingDbContext dbContext,
        NotebookTitleEncryption notebookTitleEncryption,
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
                nameParameter.Value = "EncryptNotebookTitleV1";
                stateCheckCommand.Parameters.Add(nameParameter);

                if ((bool)(await stateCheckCommand.ExecuteScalarAsync(cancellationToken) ?? false))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
            }

            var legacyTitles = new List<(int Id, string Title)>();

            await using (var selectCommand = connection.CreateCommand())
            {
                selectCommand.Transaction = transaction;
                selectCommand.CommandText =
                    """SELECT "Id", "Title" FROM "Notebooks" WHERE "Title" NOT LIKE 'dt1:%'""";

                await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    legacyTitles.Add((reader.GetInt32(0), reader.GetString(1)));
                }
            }

            await using (var updateCommand = connection.CreateCommand())
            {
                updateCommand.Transaction = transaction;
                updateCommand.CommandText =
                    """UPDATE "Notebooks" SET "Title" = @title WHERE "Id" = @id""";

                var titleParameter = updateCommand.CreateParameter();
                titleParameter.ParameterName = "@title";
                updateCommand.Parameters.Add(titleParameter);

                var idParameter = updateCommand.CreateParameter();
                idParameter.ParameterName = "@id";
                idParameter.DbType = DbType.Int32;
                updateCommand.Parameters.Add(idParameter);

                foreach (var notebook in legacyTitles)
                {
                    titleParameter.Value = notebookTitleEncryption.Encrypt(notebook.Title);
                    idParameter.Value = notebook.Id;
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
                nameParameter.Value = "EncryptNotebookTitleV1";
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
