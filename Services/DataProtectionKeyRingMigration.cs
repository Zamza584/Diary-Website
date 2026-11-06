using System.Text;
using System.Xml;
using System.Xml.Linq;
using Azure;
using Azure.Extensions.AspNetCore.DataProtection.Blobs;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NoteKeeping.Data;
using NoteKeeping.Services;

namespace NoteKeeping.Services;

public static class DataProtectionKeyRingMigration
{
    private static readonly XName KeyElementName = XName.Get("key");
    private static readonly XName RepositoryElementName = XName.Get("repository");

    public static async Task MigrateAsync(DirectoryInfo keyDirectory, Uri blobUri)
    {
        ValidateBlobUri(blobUri);
        var sourceKeys = LoadSourceKeys(keyDirectory);
        var blob = new BlobClient(blobUri, new DefaultAzureCredential());

        if (await blob.ExistsAsync())
        {
            var existingKeys = await LoadRepositoryAsync(blob);
            var additionalKeyCount = VerifySourceKeysPresent(sourceKeys, existingKeys);
            Console.WriteLine(
                $"No changes made: the existing Azure repository contains all {sourceKeys.Count} local keys unchanged" +
                (additionalKeyCount > 0 ? $" and {additionalKeyCount} additional key(s)." : "."));
            return;
        }

        var repositoryDocument = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(RepositoryElementName, sourceKeys.Values.Select(key => new XElement(key))));
        var repositoryBytes = SerializeDocument(repositoryDocument);

        try
        {
            await using var uploadStream = new MemoryStream(repositoryBytes, writable: false);
            await blob.UploadAsync(uploadStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "application/xml; charset=utf-8"
                },
                Conditions = new BlobRequestConditions
                {
                    IfNoneMatch = ETag.All
                }
            });
        }
        catch (Azure.RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            if (!await blob.ExistsAsync())
            {
                throw;
            }

            var racedRepository = await LoadRepositoryAsync(blob);
            var additionalKeyCount = VerifySourceKeysPresent(sourceKeys, racedRepository);
            Console.WriteLine(
                $"No changes made by this run: a concurrent writer created a repository containing all {sourceKeys.Count} local keys unchanged" +
                (additionalKeyCount > 0 ? $" and {additionalKeyCount} additional key(s)." : "."));
            return;
        }

        var migratedKeys = await LoadRepositoryAsync(blob);
        var additionalMigratedKeyCount = VerifySourceKeysPresent(sourceKeys, migratedKeys);
        Console.WriteLine(
            $"Migration verified: transferred and matched all {sourceKeys.Count} local key(s) in the Azure repository" +
            (additionalMigratedKeyCount > 0 ? $" with {additionalMigratedKeyCount} additional key(s)." : "."));
    }

    public static async Task ValidateAsync(
        DirectoryInfo keyDirectory,
        Uri blobUri,
        string? connectionString)
    {
        ValidateBlobUri(blobUri);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings:DefaultConnection or ConnectionStrings__DefaultConnection for read-only validation.");
        }

        var sourceKeys = LoadSourceKeys(keyDirectory);
        var blob = new BlobClient(blobUri, new DefaultAzureCredential());
        if (!await blob.ExistsAsync())
        {
            throw new InvalidOperationException(
                "The Azure key repository blob does not exist. Validation stopped without creating a key or modifying the blob.");
        }

        var repositoryKeys = await LoadRepositoryAsync(blob);
        var additionalKeyCount = VerifySourceKeysPresent(sourceKeys, repositoryKeys);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection()
            .SetApplicationName("NoteKeeping")
            .PersistKeysToAzureBlobStorage(blobUri, new DefaultAzureCredential());
        services.AddSingleton<NoteTextEncryption>();
        services.AddSingleton<NotebookTitleEncryption>();
        services.AddDbContextFactory<NoteKeepingDbContext>(options => options.UseNpgsql(connectionString));

        await using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<NoteKeepingDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync();

        var encryptedTitleCount = await CountPrefixedValuesAsync(dbContext, "\"Notebooks\"", "\"Title\"", NotebookTitleEncryption.EncryptedValuePrefix);
        var encryptedNoteCount = await CountPrefixedValuesAsync(dbContext, "\"Notes\"", "\"Text\"", NoteTextEncryption.EncryptedValuePrefix);

        if (encryptedTitleCount == 0 || encryptedNoteCount == 0)
        {
            throw new InvalidOperationException(
                $"Validation expected encrypted titles and notes, but found {encryptedTitleCount} encrypted title(s) and {encryptedNoteCount} encrypted note(s). No database values were changed.");
        }

        var titleCount = 0;
        await foreach (var title in dbContext.Notebooks
                           .AsNoTracking()
                           .Select(notebook => notebook.Title)
                           .AsAsyncEnumerable())
        {
            _ = title.Length;
            titleCount++;
        }

        var noteCount = 0;
        await foreach (var noteText in dbContext.Notes
                           .AsNoTracking()
                           .Select(note => note.Text)
                           .AsAsyncEnumerable())
        {
            _ = noteText.Length;
            noteCount++;
        }

        if (titleCount < encryptedTitleCount || noteCount < encryptedNoteCount)
        {
            throw new InvalidOperationException(
                $"Validation read {titleCount} title(s) and {noteCount} note(s), fewer than the encrypted-row counts. No database values were changed.");
        }

        Console.WriteLine(
            $"Validation succeeded: the Azure repository contains all {sourceKeys.Count} local key(s) unchanged" +
            (additionalKeyCount > 0 ? $" and {additionalKeyCount} additional key(s);" : ";") +
            $" decrypted {encryptedTitleCount} encrypted title(s) and {encryptedNoteCount} encrypted note(s) using the Azure key ring. Read {titleCount} title(s) and {noteCount} note(s) total; no database values were changed.");
    }

    private static Dictionary<string, XElement> LoadSourceKeys(DirectoryInfo keyDirectory)
    {
        if (!keyDirectory.Exists)
        {
            throw new DirectoryNotFoundException($"The local Data Protection key directory does not exist: {keyDirectory.FullName}");
        }

        var files = keyDirectory.GetFiles("*.xml", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            throw new InvalidOperationException($"No XML key files were found in {keyDirectory.FullName}.");
        }

        var keys = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            using var stream = file.OpenRead();
            var document = LoadDocument(stream);
            var root = document.Root;
            var id = root?.Attribute("id")?.Value;
            if (root?.Name != KeyElementName ||
                !Guid.TryParse(id, out _) ||
                string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException($"Key file '{file.Name}' is not a valid Data Protection key XML document.");
            }

            if (!keys.TryAdd(id, new XElement(root)))
            {
                throw new InvalidDataException($"Duplicate Data Protection key ID '{id}' was found.");
            }
        }

        return keys;
    }

    private static async Task<Dictionary<string, XElement>> LoadRepositoryAsync(BlobClient blob)
    {
        var download = await blob.DownloadContentAsync();
        using var stream = new MemoryStream(download.Value.Content.ToArray(), writable: false);
        var document = LoadDocument(stream);
        var root = document.Root;
        if (root?.Name != RepositoryElementName)
        {
            throw new InvalidDataException("The Azure blob is not an Azure Data Protection XML repository. It was not modified.");
        }

        var keys = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in root.Elements())
        {
            var id = element.Attribute("id")?.Value;
            if (element.Name != KeyElementName ||
                !Guid.TryParse(id, out _) ||
                string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException("The Azure repository contains an unexpected or invalid child element. It was not modified.");
            }

            if (!keys.TryAdd(id, new XElement(element)))
            {
                throw new InvalidDataException($"The Azure repository contains duplicate key ID '{id}'. It was not modified.");
            }
        }

        return keys;
    }

    private static int VerifySourceKeysPresent(
        IReadOnlyDictionary<string, XElement> sourceKeys,
        IReadOnlyDictionary<string, XElement> repositoryKeys)
    {
        foreach (var (id, sourceKey) in sourceKeys)
        {
            if (!repositoryKeys.TryGetValue(id, out var repositoryKey) ||
                !XNode.DeepEquals(sourceKey, repositoryKey))
            {
                throw new InvalidOperationException(
                    $"Azure repository validation failed for key ID '{id}'. The repository was not overwritten.");
            }
        }

        return repositoryKeys.Count - sourceKeys.Count;
    }

    private static XDocument LoadDocument(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static byte[] SerializeDocument(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
               {
                   Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                   Indent = false,
                   CloseOutput = false
               }))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

    private static async Task<long> CountPrefixedValuesAsync(
        NoteKeepingDbContext dbContext,
        string tableName,
        string columnName,
        string prefix)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE {columnName} LIKE @prefix";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "prefix";
        parameter.Value = prefix + "%";
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static void ValidateBlobUri(Uri blobUri)
    {
        if (!blobUri.IsAbsoluteUri ||
            blobUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(blobUri.Query) ||
            !string.IsNullOrEmpty(blobUri.UserInfo) ||
            !string.IsNullOrEmpty(blobUri.Fragment))
        {
            throw new ArgumentException(
                "The key repository URI must be an absolute HTTPS blob URI without credentials or a query string.",
                nameof(blobUri));
        }
    }
}
