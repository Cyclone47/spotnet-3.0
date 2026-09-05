namespace Spotnet.Platform;

/// <summary>
/// Secure credential storage interface.
/// On macOS, backed by Apple Keychain Services.
/// On Windows, backed by DPAPI or Windows Credential Manager.
/// </summary>
public interface ISecretStore
{
	void SetSecret(string key, string secret);
	string GetSecret(string key);
	bool DeleteSecret(string key);
}
