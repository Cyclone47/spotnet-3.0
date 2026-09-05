using System;
using System.Security.Cryptography;
using Spotnet.Helpers;
using Xunit;

namespace Spotnet.Tests
{
    /// <summary>
    /// Header import calls MakeRsa once per spot, and constructing an
    /// RSACryptoServiceProvider allocates a Windows CryptoAPI key container. These pin the
    /// caching behaviour that keeps that cost off the import path.
    /// </summary>
    public class RsaVerifierCacheTests
    {
        /// <summary>A real 1024-bit modulus, base64 as it appears in a spot header.</summary>
        private static string NewModulus()
        {
            using var rsa = new RSACryptoServiceProvider(1024);
            return Convert.ToBase64String(rsa.ExportParameters(false).Modulus);
        }

        [Fact]
        public void MakeRsa_ReturnsSameInstanceForSameModulus()
        {
            string modulus = NewModulus();

            RSACryptoServiceProvider first = SpotHelper.MakeRsa(modulus);
            RSACryptoServiceProvider second = SpotHelper.MakeRsa(modulus);

            Assert.NotNull(first);
            Assert.Same(first, second);
        }

        [Fact]
        public void MakeRsa_ReturnsDistinctVerifiersForDistinctModuli()
        {
            RSACryptoServiceProvider a = SpotHelper.MakeRsa(NewModulus());
            RSACryptoServiceProvider b = SpotHelper.MakeRsa(NewModulus());

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.NotSame(a, b);
        }

        [Fact]
        public void MakeRsa_RejectsMalformedModuli()
        {
            Assert.Null(SpotHelper.MakeRsa(null));
            Assert.Null(SpotHelper.MakeRsa(""));
            // Not a multiple of 4, so not valid base64.
            Assert.Null(SpotHelper.MakeRsa("abc"));
        }

        [Fact]
        public void CachedVerifier_StillValidatesASignature()
        {
            // The cache must not change the verification result: sign with a private key,
            // then verify through the cached public-only verifier twice.
            using var signer = new RSACryptoServiceProvider(1024);
            RSAParameters publicPart = signer.ExportParameters(false);
            string modulus = Convert.ToBase64String(publicPart.Modulus);

            byte[] payload = SpotHelper.MakeLatin("Ubuntu 24.04 LTS<user@spot.net>");
            byte[] hash;
            using (var sha = new SHA1Managed())
            {
                hash = sha.ComputeHash(payload);
            }
            byte[] signature = signer.SignHash(hash, null);

            RSACryptoServiceProvider verifier = SpotHelper.MakeRsa(modulus);
            Assert.NotNull(verifier);
            Assert.True(verifier.VerifyHash(hash, null, signature));

            // Second call comes from the cache and must behave identically.
            RSACryptoServiceProvider again = SpotHelper.MakeRsa(modulus);
            Assert.Same(verifier, again);
            Assert.True(again.VerifyHash(hash, null, signature));

            // And must still reject a tampered hash.
            hash[0] ^= 0xFF;
            Assert.False(again.VerifyHash(hash, null, signature));
        }
    }
}
