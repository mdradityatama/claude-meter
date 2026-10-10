using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeMeter.Core;

public enum LoginState
{
    Valid,
    /// <summary>The token endpoint answered invalid_grant; only a new /login helps.</summary>
    Expired,
}

/// <param name="CredentialsJson">The whole <c>.credentials.json</c>; only refresh fields are ever spliced.</param>
/// <param name="OAuthAccountJson">The <c>oauthAccount</c> value from the global config, verbatim.</param>
/// <param name="ExpiryWarnedFor">The <c>refreshTokenExpiresAt</c> (ms) already warned about.</param>
internal sealed record StoredAccount(
    AccountKey Key,
    string Email,
    string? Plan,
    byte[] CredentialsJson,
    byte[] OAuthAccountJson,
    DateTimeOffset AddedAt,
    DateTimeOffset? LastActiveAt,
    UsageSnapshot? LastKnownUsage,
    DateTimeOffset? LastKnownAt,
    LoginState LoginState,
    long? ExpiryWarnedFor)
{
    [JsonIgnore]
    public CredentialsInfo? Credentials => CredentialsInfo.Parse(CredentialsJson);

    public static StoredAccount Create(LiveLogin live, DateTimeOffset now) => new(
        live.Account.Key, live.Account.Email, live.Credentials.SubscriptionType, live.CredentialsJson, live.Account.Raw,
        AddedAt: now, LastActiveAt: now, LastKnownUsage: null, LastKnownAt: null, LoginState.Valid, ExpiryWarnedFor: null);

    /// <summary>A fresh capture of this identity; a new /login also clears an expired state.</summary>
    public StoredAccount WithLiveLogin(LiveLogin live) => this with
    {
        Email = live.Account.Email,
        Plan = live.Credentials.SubscriptionType ?? Plan,
        CredentialsJson = live.CredentialsJson,
        OAuthAccountJson = live.Account.Raw,
        LoginState = LoginState.Valid,
    };
}

internal enum StoreLoadResult
{
    Empty,
    Loaded,
    /// <summary>The file could not be read; it was renamed aside and the store starts empty.</summary>
    SetAside,
}

/// <summary>Saved accounts in one DPAPI-encrypted file, written atomically.</summary>
internal sealed class AccountStore(string path, IDataProtector protector)
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    private List<StoredAccount> _accounts = [];

    /// <summary>In <see cref="StoredAccount.AddedAt"/> order; this is the display order.</summary>
    public IReadOnlyList<StoredAccount> Accounts => _accounts;

    public bool HasUnsavedChanges { get; private set; }

    /// <summary>The last <see cref="TrySave"/> failed; the in-memory state is newer than the file.</summary>
    public bool LastSaveFailed { get; private set; }

    public StoredAccount? Find(AccountKey key) => _accounts.Find(a => a.Key == key);

    public StoredAccount? FindByCredentials(ReadOnlySpan<byte> credentials)
    {
        foreach (var account in _accounts)
        {
            if (account.CredentialsJson.AsSpan().SequenceEqual(credentials))
                return account;
        }

        return null;
    }

    public void Upsert(StoredAccount account)
    {
        _accounts = [.. _accounts.Where(a => a.Key != account.Key).Append(account).OrderBy(a => a.AddedAt)];
        HasUnsavedChanges = true;
    }

    public bool Remove(AccountKey key)
    {
        var removed = _accounts.RemoveAll(a => a.Key == key) > 0;
        HasUnsavedChanges |= removed;
        return removed;
    }

    public StoreLoadResult Load()
    {
        if (LiveFiles.ReadOrNull(path) is not { } data)
            return StoreLoadResult.Empty;

        try
        {
            var file = JsonSerializer.Deserialize<StoreFile>(protector.Unprotect(data), Options);
            if (file is not { Version: 1, Accounts: not null })
                throw new FormatException("Unknown store format.");
            _accounts = [.. file.Accounts.OrderBy(a => a.AddedAt)];
            return StoreLoadResult.Loaded;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or NotSupportedException)
        {
            // Never overwrite what can't be read. It stays encrypted, so keeping it is safe.
            File.Move(path, $"{path}.unreadable-{DateTime.UtcNow:yyyyMMddHHmmss}");
            return StoreLoadResult.SetAside;
        }
    }

    public bool TrySave()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.Write(path, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(new StoreFile(1, _accounts), Options)));
            HasUnsavedChanges = false;
            LastSaveFailed = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            LastSaveFailed = true;
            return false;
        }
    }

    private sealed record StoreFile(int Version, List<StoredAccount> Accounts);
}
