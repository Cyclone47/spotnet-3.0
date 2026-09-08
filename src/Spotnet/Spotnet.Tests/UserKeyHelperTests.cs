using System;
using System.Security.Cryptography;
using Spotnet.Helpers;
using Xunit;

namespace Spotnet.Tests;

public class UserKeyHelperTests
{
    [Fact]
    public void GetKeyReturnsNonNullRsaProviderWithExpectedKeySize()
    {
        RSACryptoServiceProvider key = UserKeyHelper.GetKey();
        Assert.NotNull(key);
        Assert.Equal(384, key.KeySize);
    }

    [Fact]
    public void GetModulusReturnsNonEmptyValidBase64()
    {
        string modulus = UserKeyHelper.GetModulus();
        Assert.False(string.IsNullOrEmpty(modulus));
        byte[] bytes = Convert.FromBase64String(modulus);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void GetModulusUriCompatableDoesNotContainUrlUnsafeCharacters()
    {
        string uriCompat = UserKeyHelper.GetModulusUriCompatable();
        Assert.False(string.IsNullOrEmpty(uriCompat));
        Assert.DoesNotContain("+", uriCompat);
        Assert.DoesNotContain("/", uriCompat);
    }

    [Fact]
    public void GetKeyIsIdempotent()
    {
        RSACryptoServiceProvider key1 = UserKeyHelper.GetKey();
        RSACryptoServiceProvider key2 = UserKeyHelper.GetKey();
        Assert.Same(key1, key2);
    }
}
