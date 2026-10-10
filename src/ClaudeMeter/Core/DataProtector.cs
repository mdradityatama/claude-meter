using System.Security.Cryptography;

namespace ClaudeMeter.Core;

internal interface IDataProtector
{
    byte[] Protect(byte[] plaintext);

    /// <exception cref="CryptographicException">Not decryptable by this Windows user.</exception>
    byte[] Unprotect(byte[] data);
}

/// <summary>DPAPI, current user. The entropy ties the data to this app.</summary>
internal sealed class DpapiProtector : IDataProtector
{
    private static readonly byte[] Entropy = "ClaudeMeter.accounts.v1"u8.ToArray();

    public byte[] Protect(byte[] plaintext) => ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
