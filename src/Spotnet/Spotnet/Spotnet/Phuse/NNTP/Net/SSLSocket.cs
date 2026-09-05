using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using NLog;
using Spotnet.Properties;

namespace Spotnet.Phuse.NNTP.Net;

internal class SSLSocket : SocketBase
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private bool ValidateRemoteCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors policyErrors)
	{
		if (policyErrors == SslPolicyErrors.None)
		{
			return true;
		}
		string host = DestinationServer?.Host ?? "news server";
		if (Settings.Default.AllowInvalidServerCertificate)
		{
			Log.Warn("Certificate for {0} failed validation ({1}) but was accepted because 'Allow invalid server certificate' is on.", host, policyErrors);
			return true;
		}
		Log.Error("Certificate for {0} failed validation: {1}. If this provider uses a self-signed certificate, enable 'Allow invalid server certificate' in the connection settings.", host, policyErrors);
		return false;
	}

	protected override void InitSocketStream()
	{
		// RequireEncryption rejects the null-cipher suites that AllowNoEncryption
		// permitted, so a negotiated connection is always actually encrypted.
		SslStream sslStream = new SslStream(SocketClient.GetStream(), leaveInnerStreamOpen: false, ValidateRemoteCertificate, null, EncryptionPolicy.RequireEncryption);
		SocketStream = sslStream;
		// SslProtocols.None means "let the OS pick the best protocol both ends support".
		// The old SslProtocols.Default pinned this to SSL 3.0 and TLS 1.0 on .NET
		// Framework, which providers are actively turning off.
		sslStream.AuthenticateAsClient(DestinationServer.Host, new X509CertificateCollection(), SslProtocols.None, checkCertificateRevocation: false);
	}
}
