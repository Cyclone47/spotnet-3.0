using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NLog;
using Spotnet.Mac.DAL;

namespace Spotnet.Mac.Services;

/// <summary>
/// Manages the user's personal RSA key pair used for signing posts (comments, complaints, spots).
/// Stores the key in the userkey table in the local spots database, matching Windows UserKeyHelper.
/// </summary>
public interface IUserKeyService
{
    Task<RSA> GetOrCreateUserRsaKeyAsync();
    Task<string> GetUserModulusBase64Async();
    Task<string> GetUserKeyXmlAsync(bool includePrivateParameters = false);
}

public class UserKeyService : IUserKeyService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly SpotDatabaseService _dbService;
    private RSA? _cachedRsa;
    private string? _cachedModulus;
    private readonly object _lock = new();

    public UserKeyService(SpotDatabaseService dbService)
    {
        _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
    }

    public async Task<RSA> GetOrCreateUserRsaKeyAsync()
    {
        lock (_lock)
        {
            if (_cachedRsa != null)
            {
                var copy = RSA.Create(2048);
                copy.ImportParameters(_cachedRsa.ExportParameters(includePrivateParameters: true));
                return copy;
            }
        }

        string? existingKeyXml = await _dbService.GetUserKeyXmlAsync();
        var rsa = RSA.Create(2048);

        if (!string.IsNullOrEmpty(existingKeyXml))
        {
            try
            {
                rsa.FromXmlString(existingKeyXml);
                lock (_lock)
                {
                    _cachedRsa = rsa;
                    _cachedModulus = Convert.ToBase64String(rsa.ExportParameters(includePrivateParameters: false).Modulus!);
                }
                var copy = RSA.Create(2048);
                copy.ImportParameters(rsa.ExportParameters(includePrivateParameters: true));
                return copy;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to load existing RSA key from database, generating new key.");
            }
        }

        // Generate new key and store in SQLite
        string newKeyXml = rsa.ToXmlString(includePrivateParameters: true);
        await _dbService.SetUserKeyXmlAsync(newKeyXml);

        lock (_lock)
        {
            _cachedRsa = rsa;
            _cachedModulus = Convert.ToBase64String(rsa.ExportParameters(includePrivateParameters: false).Modulus!);
        }

        var res = RSA.Create(2048);
        res.ImportParameters(rsa.ExportParameters(includePrivateParameters: true));
        return res;
    }

    public async Task<string> GetUserModulusBase64Async()
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_cachedModulus))
            {
                return _cachedModulus;
            }
        }

        using var rsa = await GetOrCreateUserRsaKeyAsync();
        lock (_lock)
        {
            return _cachedModulus ?? "";
        }
    }

    public async Task<string> GetUserKeyXmlAsync(bool includePrivateParameters = false)
    {
        using var rsa = await GetOrCreateUserRsaKeyAsync();
        return rsa.ToXmlString(includePrivateParameters);
    }
}
