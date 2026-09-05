using System.IO;

namespace Spotnet.Phuse.NNTP.Net;

internal struct WorkArgs
{
	public int Code;

	public Stream Data;

	public byte[] Bytes;

	public int Offset;

	public int BytesReceived;

	public string Message;
}
