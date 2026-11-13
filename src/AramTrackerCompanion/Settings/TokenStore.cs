using System.Security.Cryptography;
using System.Text;

namespace AramTrackerCompanion.Settings;

internal sealed class TokenStore(string directory)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ARAMTrackerCompanion.Token.v1");

    private readonly string _path = Path.Combine(directory, "token.bin");

    public string? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Stored token could not be read", exception);
            return null;
        }
    }

    public void Save(string token)
    {
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);
        AtomicFile.WriteAllBytes(_path, cipher);
    }

    public void Clear()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Stored token could not be deleted", exception);
        }
    }
}
