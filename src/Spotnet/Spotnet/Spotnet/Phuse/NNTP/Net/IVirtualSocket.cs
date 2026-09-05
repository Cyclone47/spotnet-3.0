using System;
using System.IO;

namespace Spotnet.Phuse.NNTP.Net;

internal interface IVirtualSocket
{
	event EventHandler<WorkArgs> Received;

	event EventHandler<WorkArgs> Connected;

	event EventHandler<WorkArgs> Disconnected;

	bool Receive();

	bool IsConnected();

	bool Send(Stream bData, int expectedBytesReturned = -1);

	bool Close(int iCode, string sError);

	bool Connect(VirtualServer svr);

	void ClearData();
}
