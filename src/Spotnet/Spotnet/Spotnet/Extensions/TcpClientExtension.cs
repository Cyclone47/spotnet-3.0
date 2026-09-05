using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Spotnet.Extensions;

public static class TcpClientExtension
{
	public static TcpState GetState(this TcpClient tcpClient)
	{
		return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().SingleOrDefault((TcpConnectionInformation x) => x.LocalEndPoint.Equals(tcpClient.Client.LocalEndPoint))?.State ?? TcpState.Unknown;
	}
}
