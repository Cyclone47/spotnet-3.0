using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SpotnetEnc;

/// <summary>
/// Cross-platform RSA signature and hashcash verification for Spotnet headers and spots.
/// Replicates the verification logic from Windows Spotnet (Worker.cs and SpotHelper.cs).
/// </summary>
public static class SpotnetSignatureVerifier
{
    private static readonly byte[] RsaExponent = new byte[] { 1, 0, 1 }; // 65537 (0x010001)
    private static readonly ConcurrentDictionary<string, RSA> RsaCache = new();
    private const int RsaCacheLimit = 1000;

    /// <summary>
    /// Normalizes Spotnet's URL-safe base64 encoding and restores padding.
    /// Replicates SpotHelper.UnSpecialString + SpotHelper.FixPadding.
    /// </summary>
    public static string UnescapeBase64(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        string plain = value.Replace("-s", "/", StringComparison.Ordinal)
                            .Replace("-p", "+", StringComparison.Ordinal);

        return (plain.Length % 4) switch
        {
            1 => plain + "===",
            2 => plain + "==",
            3 => plain + "=",
            _ => plain
        };
    }

    /// <summary>
    /// Creates or retrieves a cached RSA public key verifier for a given base64 modulus.
    /// Returns null if the modulus is malformed.
    /// </summary>
    public static RSA? MakeRsa(string? sModulus)
    {
        if (string.IsNullOrWhiteSpace(sModulus)) return null;

        string cleanModulus = UnescapeBase64(sModulus.Trim());
        if (cleanModulus.Length < 50 || cleanModulus.Length % 4 != 0)
        {
            return null;
        }

        if (RsaCache.TryGetValue(cleanModulus, out var cached))
        {
            return cached;
        }

        RSA? rsa = CreateRsa(cleanModulus);
        if (rsa != null && RsaCache.Count < RsaCacheLimit)
        {
            RsaCache[cleanModulus] = rsa;
        }

        return rsa;
    }

    private static RSA? CreateRsa(string cleanModulus)
    {
        try
        {
            byte[] modulusBytes = Convert.FromBase64String(cleanModulus);
            var rsa = RSA.Create();
            var parameters = new RSAParameters
            {
                Exponent = RsaExponent,
                Modulus = modulusBytes
            };
            rsa.ImportParameters(parameters);
            return rsa;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Clears the RSA verifier cache (primarily used in tests).
    /// </summary>
    public static void ClearCache()
    {
        RsaCache.Clear();
    }

    /// <summary>
    /// Checks Spotnet's proof-of-work (hashcash) requirement:
    /// The SHA1 digest of the Message-ID (in Latin-1) must have its first 2 bytes equal to 0.
    /// </summary>
    public static bool CheckProofOfWork(string messageId, out byte[] hash)
    {
        string formattedMsgId = messageId.StartsWith('<') ? messageId : $"<{messageId}>";
        byte[] msgBytes = Encoding.Latin1.GetBytes(formattedMsgId);
        hash = SHA1.HashData(msgBytes);

        return hash.Length >= 2 && hash[0] == 0 && hash[1] == 0;
    }

    /// <summary>
    /// Verifies a user signature on a Message-ID against a public key modulus.
    /// </summary>
    public static bool VerifyUserSignature(string rawModulus, string rawUserSignature, string messageId)
    {
        if (string.IsNullOrWhiteSpace(rawModulus) || string.IsNullOrWhiteSpace(rawUserSignature))
        {
            return false;
        }

        if (!CheckProofOfWork(messageId, out byte[] hash))
        {
            return false;
        }

        RSA? rsa = MakeRsa(rawModulus);
        if (rsa == null) return false;

        try
        {
            byte[] signatureBytes = Convert.FromBase64String(UnescapeBase64(rawUserSignature));
            return rsa.VerifyHash(hash, signatureBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies the cryptographic signature of an NNTP overview header line.
    ///
    /// Mirrors Worker.DoWork, VerifySignOfSpotnetSpot and VerifySignOfSpotFromKeysFile:
    /// - Key 1: Unsigned legacy spots; always returns true.
    /// - Key 7: User-signed spots verified against poster's modulus.
    /// - Key 2..8: Spots signed by trusted keys from keys.xml; optional user signature verified against modulus.
    /// </summary>
    public static (bool IsValid, string? ValidModulus) VerifySpotHeader(
        string poster,
        string title,
        string fromHeader,
        string messageId,
        byte keyId,
        IReadOnlyDictionary<int, string>? trustedKeys = null)
    {
        // Key 1 spots are legacy (pre-2011) and contain no cryptographic signatures.
        if (keyId == 1)
        {
            string? legacyMod = ExtractModulusOnly(fromHeader);
            return (true, legacyMod);
        }

        if (string.IsNullOrWhiteSpace(fromHeader) || string.IsNullOrWhiteSpace(messageId))
        {
            return (false, null);
        }

        int open = fromHeader.IndexOf('<', StringComparison.Ordinal);
        int at = fromHeader.IndexOf('@', StringComparison.Ordinal);
        int close = fromHeader.IndexOf('>', StringComparison.Ordinal);

        if (open < 0 || at <= open || close <= at)
        {
            return (false, null);
        }

        // Credentials between '<' and '@': "<modulus>.<userSignature>" or "<modulus>"
        string credentials = fromHeader.Substring(open + 1, at - open - 1);
        string modulus = "";
        string userSignature = "";

        if (credentials.Length > 50)
        {
            int dot = credentials.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0)
            {
                modulus = UnescapeBase64(credentials);
            }
            else
            {
                modulus = UnescapeBase64(credentials[..dot]);
                userSignature = UnescapeBase64(credentials[(dot + 1)..]);
            }
        }

        // Address part between '@' and '>': "CATS.FILESIZE.?.STAMP.?.TAG.SIGNATURE"
        string address = fromHeader.Substring(at + 1, close - at - 1);
        string[] addressParts = address.Split('.');
        if (addressParts.Length < 7)
        {
            return (false, null);
        }

        string signaturePart = addressParts.Last();
        if (string.IsNullOrWhiteSpace(signaturePart))
        {
            return (false, null);
        }

        string addressBeforeLastDot = address.Substring(0, address.Length - signaturePart.Length - 1);

        string posterPart = !string.IsNullOrWhiteSpace(poster) ? poster : fromHeader[..open].Trim();
        byte[] payloadBytes = Encoding.Latin1.GetBytes(title + addressBeforeLastDot + posterPart);
        byte[] payloadHash = SHA1.HashData(payloadBytes);

        // Key 7: User-signed spot
        if (keyId == 7)
        {
            if (!CheckProofOfWork(messageId, out byte[] msgIdHash))
            {
                return (false, null);
            }

            if (modulus.Length < 50)
            {
                return (false, null);
            }

            RSA? userRsa = MakeRsa(modulus);
            if (userRsa == null) return (false, null);

            try
            {
                if (userSignature.Length > 0)
                {
                    byte[] userSigBytes = Convert.FromBase64String(UnescapeBase64(userSignature));
                    if (!userRsa.VerifyHash(msgIdHash, userSigBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1))
                    {
                        return (false, null);
                    }
                }
                else
                {
                    byte[] sigBytes = Convert.FromBase64String(UnescapeBase64(signaturePart));
                    if (!userRsa.VerifyHash(payloadHash, sigBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1))
                    {
                        return (false, null);
                    }
                }

                return (true, modulus);
            }
            catch
            {
                return (false, null);
            }
        }

        // Key 2..8: Keys from keys.xml
        if (trustedKeys != null && trustedKeys.TryGetValue(keyId, out string? trustedModulus) && !string.IsNullOrWhiteSpace(trustedModulus))
        {
            byte[]? msgIdHash = null;
            if (userSignature.Length > 0)
            {
                if (!CheckProofOfWork(messageId, out msgIdHash))
                {
                    return (false, null);
                }
            }

            byte[] rgbSignature;
            try
            {
                rgbSignature = Convert.FromBase64String(UnescapeBase64(signaturePart));
            }
            catch
            {
                return (false, null);
            }

            RSA? trustedRsa = MakeRsa(trustedModulus);
            if (trustedRsa == null || !trustedRsa.VerifyHash(payloadHash, rgbSignature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1))
            {
                return (false, null);
            }

            // If user signature is present on a trusted key spot, verify it against poster's modulus
            if (userSignature.Length > 0 && msgIdHash != null)
            {
                RSA? userRsa = MakeRsa(modulus);
                if (userRsa == null) return (false, null);

                try
                {
                    byte[] userSigBytes = Convert.FromBase64String(UnescapeBase64(userSignature));
                    if (!userRsa.VerifyHash(msgIdHash, userSigBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1))
                    {
                        return (false, null);
                    }
                }
                catch
                {
                    return (false, null);
                }
            }

            return (true, modulus.Length > 0 ? modulus : null);
        }

        // Key > 1 but not Key 7 and trusted key is not available
        return (false, null);
    }

    private static string? ExtractModulusOnly(string fromHeader)
    {
        int open = fromHeader.IndexOf('<', StringComparison.Ordinal);
        int at = fromHeader.IndexOf('@', StringComparison.Ordinal);
        if (open < 0 || at <= open) return null;

        string cred = fromHeader.Substring(open + 1, at - open - 1);
        if (cred.Length <= 50) return null;

        int dot = cred.IndexOf('.', StringComparison.Ordinal);
        return UnescapeBase64(dot < 0 ? cred : cred[..dot]);
    }
}
