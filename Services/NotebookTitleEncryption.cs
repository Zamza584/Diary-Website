using Microsoft.AspNetCore.DataProtection;

namespace NoteKeeping.Services;

public sealed class NotebookTitleEncryption
{
    public const string EncryptedValuePrefix = "dt1:";

    private readonly IDataProtector _protector;

    public NotebookTitleEncryption(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("NoteKeeping.NotebookTitle.v1");
    }

    public string Encrypt(string plaintext)
    {
        return EncryptedValuePrefix + _protector.Protect(plaintext);
    }

    public string Decrypt(string storedValue)
    {
        return storedValue.StartsWith(EncryptedValuePrefix, StringComparison.Ordinal)
            ? _protector.Unprotect(storedValue[EncryptedValuePrefix.Length..])
            : storedValue;
    }
}
