namespace Spotnet.Phuse.NNTP.Net;

internal class Socket : SocketBase
{
	protected override void InitSocketStream()
	{
		SocketStream = SocketClient.GetStream();
	}
}
