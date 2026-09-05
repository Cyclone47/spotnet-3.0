using System.Collections.Concurrent;

namespace Spotnet.Platform;

/// <summary>
/// In-memory secret store used for unit tests or fallback when OS keychain is unavailable.
/// </summary>
public class InMemorySecretStore : ISecretStore
{
	private readonly ConcurrentDictionary<string, string> _secrets = new ConcurrentDictionary<string, string>();

	public void SetSecret(string key, string secret)
	{
		if (secret == null)
		{
			DeleteSecret(key);
		}
		else
		{
			_secrets[key] = secret;
		}
	}

	public string GetSecret(string key)
	{
		_secrets.TryGetValue(key, out string val);
		return val;
	}

	public bool DeleteSecret(string key)
	{
		return _secrets.TryRemove(key, out _);
	}
}
