using System;
using Spotnet.Mac.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

public class MacKeychainTests
{
    [Fact]
    public void SecretStore_SetGetDelete_RoundTripsSuccessfully()
    {
        var store = new MacKeychainSecretStore();
        string testKey = "Spotnet_UnitTest_" + Guid.NewGuid().ToString("N");
        string testSecret = "SuperSecretPassword123!@#";

        try
        {
            // 1. Set secret
            store.SetSecret(testKey, testSecret);

            // 2. Get secret
            string? retrieved = store.GetSecret(testKey);
            Assert.Equal(testSecret, retrieved);

            // 3. Delete secret
            bool deleted = store.DeleteSecret(testKey);
            Assert.True(deleted);

            // 4. Verify deleted
            string? afterDelete = store.GetSecret(testKey);
            Assert.Null(afterDelete);
        }
        finally
        {
            store.DeleteSecret(testKey);
        }
    }
}
