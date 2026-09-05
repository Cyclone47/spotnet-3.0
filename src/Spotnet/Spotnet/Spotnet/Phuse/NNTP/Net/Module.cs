using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using Spotnet.Extensions;
using Spotnet.Model;

namespace Spotnet.Phuse.NNTP.Net;

internal static class Module
{
	private static readonly Encoding CEnc = Encoding.GetEncoding("iso-8859-1");

	internal static Random Random { get; } = new Random();


	internal static XmlWriterSettings WriterSettings => new XmlWriterSettings
	{
		Indent = true,
		IndentChars = "\t",
		Encoding = CEnc
	};

	internal static XmlReaderSettings ReaderSettings => new XmlReaderSettings
	{
		DtdProcessing = DtdProcessing.Ignore,
		XmlResolver = null
	};

	internal static void Safe32(ref long sLong, long lVal)
	{
		if (lVal != Interlocked.Read(ref sLong))
		{
			long num;
			do
			{
				num = Interlocked.Read(ref sLong);
			}
			while (num != Interlocked.CompareExchange(ref sLong, lVal, num));
		}
	}

	internal static void Add32(ref long sLong, long incr)
	{
		long num;
		long value;
		do
		{
			num = Interlocked.Read(ref sLong);
			value = num + incr;
		}
		while (num != Interlocked.CompareExchange(ref sLong, value, num));
	}

	internal static void UpdateValue(ConcurrentDictionary<int, int> cCol, int lIndex, int lValue)
	{
		cCol.AddOrUpdate(lIndex, lValue, (int key, int oldValue) => oldValue + lValue);
	}

	internal static int GetValue(ConcurrentDictionary<int, int> cCol, int lIndex)
	{
		if (!cCol.ContainsKey(lIndex))
		{
			return -1;
		}
		int value;
		while (!cCol.TryGetValue(lIndex, out value))
		{
			if (!cCol.ContainsKey(lIndex))
			{
				return -1;
			}
		}
		return value;
	}

	internal static Stream GetStream(string sData, Encoding enc = null)
	{
		return new MemoryStream(enc?.GetBytes(sData) ?? CEnc.GetBytes(sData), writable: false);
	}

	internal static byte[] GetBytes(Stream bData, long offset = 0L, long count = -1L)
	{
		if (bData == null)
		{
			return null;
		}
		if (count < 0)
		{
			count = bData.Length;
		}
		byte[] array = new byte[count];
		bData.Position = offset;
		int read = ReadAtMost(bData, array, 0, (int)count);
		if (read < count)
		{
			// A single Read is not required to return everything asked for. It happens to
			// on a MemoryStream, which is what callers pass today, but returning a
			// zero-padded buffer for anything else would corrupt article data silently.
			Array.Resize(ref array, read);
		}
		return array;
	}

	/// <summary>
	/// Reads until <paramref name="count"/> bytes have been collected or the stream ends,
	/// and returns how many were actually read.
	/// </summary>
	private static int ReadAtMost(Stream stream, byte[] buffer, int offset, int count)
	{
		int total = 0;
		while (total < count)
		{
			int read = stream.Read(buffer, offset + total, count - total);
			if (read <= 0)
			{
				break;
			}
			total += read;
		}
		return total;
	}

	internal static string GetString(Stream bData, long offset = 0L, long count = -1L)
	{
		if (bData == null)
		{
			return "";
		}
		return CEnc.GetString(GetBytes(bData, offset, count));
	}

	internal static string GetString(byte[] bytes, int index, int count)
	{
		if (index < 0)
		{
			index = 0;
		}
		if (index > bytes.Length || count < 0)
		{
			return "";
		}
		if (count > bytes.Length - index)
		{
			count = bytes.Length - index;
		}
		return CEnc.GetString(bytes, index, count);
	}

	internal static StreamReader GetReader(Stream bData)
	{
		return new StreamReader(bData, CEnc, detectEncodingFromByteOrderMarks: false);
	}

	internal static string GetFirstLine(Stream data)
	{
		int num = 0;
		data.Position = 0L;
		int num2;
		do
		{
			num2 = data.ReadByte();
			num++;
		}
		while (num2 > 0 && num2 != 13);
		return GetString(data, 0L, num - 1);
	}

	internal static string GetFirstLine(byte[] bData, int offset, int count)
	{
		int num = offset + count;
		int i;
		for (i = offset; i < num && bData[i] != 13; i++)
		{
		}
		return CEnc.GetString(bData, 0, i);
	}

	internal static string GetFirstLine(string str)
	{
		int num = str.IndexOf(Environment.NewLine, StringComparison.Ordinal);
		if (num != -1)
		{
			return str.Substring(0, num);
		}
		return str;
	}

	private static void CopyTo(Stream src, Stream dest)
	{
		byte[] array = new byte[4096];
		int count;
		while ((count = src.Read(array, 0, array.Length)) != 0)
		{
			dest.Write(array, 0, count);
		}
	}

	internal static Stream UnzipResponse(Stream data)
	{
		long num = FindPosition(data, CEnc.GetBytes("\r\n"));
		byte[] array = new byte[num + 2];
		data.Position = 0L;
		ReadAtMost(data, array, 0, array.Length);
		int num2 = (int)(data.Length - (num + 2) - 3);
		byte[] array2 = new byte[num2];
		// Fill the compressed payload completely before handing it to zlib - a short
		// read here would surface as a corrupt-stream error rather than a truncation.
		ReadAtMost(data, array2, 0, num2);
		MemoryStream memoryStream = new MemoryStream();
		memoryStream.Write(array, 0, array.Length);
		byte[] array3 = UncompressZlib(array2);
		byte[] bytes = CEnc.GetBytes("\r\n");
		if (array3.Length > bytes.Length)
		{
			memoryStream.Write(array3, 0, array3.Length);
			if (!CEnc.GetString(array3, array3.Length - bytes.Length, bytes.Length).Equals("\r\n"))
			{
				memoryStream.Write(bytes, 0, bytes.Length);
			}
		}
		else
		{
			memoryStream.Write(bytes, 0, bytes.Length);
		}
		byte[] bytes2 = CEnc.GetBytes(".\r\n");
		memoryStream.Write(bytes2, 0, bytes2.Length);
		return memoryStream;
	}

	private static byte[] UncompressZlib(byte[] compressed)
	{
		using MemoryStream input = new MemoryStream(compressed, writable: false);
		using InflaterInputStream inflater = new InflaterInputStream(input);
		using MemoryStream output = new MemoryStream();
		CopyTo(inflater, output);
		return output.ToArray();
	}

	private static long FindPosition(Stream stream, byte[] byteSequence)
	{
		if (byteSequence.Length > stream.Length)
		{
			return -1L;
		}
		byte[] array = new byte[byteSequence.Length];
		stream.Position = 0L;
		BufferedStream bufferedStream = new BufferedStream(stream, byteSequence.Length);
		while (bufferedStream.Read(array, 0, byteSequence.Length) == byteSequence.Length)
		{
			if (byteSequence.SequenceEqual(array))
			{
				return bufferedStream.Position - byteSequence.Length;
			}
			bufferedStream.Position -= byteSequence.Length - 1;
		}
		return -1L;
	}

	internal static string GetXFeatureParams(string data)
	{
		int num = -1;
		int num2 = -1;
		for (int i = 0; i < data.Length; i++)
		{
			if (data[i] == '[')
			{
				num = i;
			}
			if (num != -1 && data[i] == ']')
			{
				num2 = i;
			}
		}
		if (num2 == -1 || num2 - num <= 1)
		{
			return null;
		}
		return data.Substring(num + 1, num2 - num - 1);
	}

	internal static string VbLeft(string sText, int iLength)
	{
		if (sText.IsNullOrEmpty())
		{
			return "";
		}
		if (iLength <= 0)
		{
			return "";
		}
		if (sText.Length <= iLength)
		{
			return sText;
		}
		return sText.Substring(0, iLength);
	}

	internal static bool IsNumeric(object expression)
	{
		double result;
		if (expression != null)
		{
			return double.TryParse(expression.ToString(), out result);
		}
		return false;
	}

	internal static WaitHandle WaitList(List<WaitHandle> wList, int timeOut = -1)
	{
		WaitHandle[] array = wList.ToArray();
		int num;
		if (timeOut > 0)
		{
			num = WaitHandle.WaitAny(array, timeOut);
			if (num == 258)
			{
				return null;
			}
		}
		else
		{
			num = 258;
			while (num == 258 && !Sys.IsShutdownRequested)
			{
				num = WaitHandle.WaitAny(array, 1000);
			}
			if (Sys.IsShutdownRequested)
			{
				return null;
			}
		}
		return array[num];
	}

	internal static int GetCode(string sLine)
	{
		if (!int.TryParse(VbLeft(sLine, 3), out var result))
		{
			return 512;
		}
		return result;
	}

	internal static string ReadLog(ConcurrentQueue<string> zLog, int maxLines = -1)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (zLog.IsEmpty)
		{
			return null;
		}
		using (IEnumerator<string> enumerator = zLog.GetEnumerator())
		{
			int num = zLog.Count - maxLines;
			if (num < maxLines || maxLines == -1)
			{
				num = 0;
			}
			int num2 = 0;
			while (enumerator.MoveNext())
			{
				num2++;
				if (num2 >= num)
				{
					string current = enumerator.Current;
					if (!current.IsNullOrEmpty())
					{
						stringBuilder.AppendLine("");
						stringBuilder.Append("\t" + current);
					}
				}
			}
		}
		return stringBuilder.ToString();
	}

	internal static void XmlToWriter(XmlWriter xw, string xml)
	{
		byte[] bytes = CEnc.GetBytes(xml);
		MemoryStream memoryStream = new MemoryStream();
		memoryStream.Write(bytes, 0, bytes.Length);
		memoryStream.Seek(0L, SeekOrigin.Begin);
		XmlReader xmlReader = XmlReader.Create(memoryStream);
		while (xmlReader.Read())
		{
			switch (xmlReader.NodeType)
			{
			case XmlNodeType.Element:
				xw.WriteStartElement(xmlReader.Name);
				if (xmlReader.HasAttributes)
				{
					for (int i = 0; i < xmlReader.AttributeCount; i++)
					{
						xmlReader.MoveToAttribute(i);
						xw.WriteAttributeString(xmlReader.Name, xmlReader.Value);
					}
				}
				if (xmlReader.IsEmptyElement)
				{
					xw.WriteEndElement();
				}
				break;
			case XmlNodeType.Text:
				xw.WriteString(xmlReader.Value);
				break;
			case XmlNodeType.EndElement:
				xw.WriteEndElement();
				break;
			}
		}
	}

	internal static string MakeMsg(string sCode, string sMsg)
	{
		StringBuilder stringBuilder = new StringBuilder();
		XmlWriter xmlWriter = XmlWriter.Create(stringBuilder, new XmlWriterSettings
		{
			OmitXmlDeclaration = true,
			Indent = true,
			IndentChars = "\t",
			Encoding = CEnc
		});
		xmlWriter.WriteStartElement("warning");
		xmlWriter.WriteElementString("code", CleanString(sCode));
		xmlWriter.WriteElementString("date", DateTime.UtcNow.ToString("dd-mm-yyyy hh:MM:ss"));
		xmlWriter.WriteElementString("message", CleanString(sMsg));
		xmlWriter.WriteEndElement();
		xmlWriter.Flush();
		return stringBuilder.ToString();
	}

	internal static string SafeString(string sIn, bool bSanitize = true)
	{
		string text = sIn;
		if ((text.Contains("<") || text.Contains(">") || text.Contains("&")) && bSanitize)
		{
			text = text.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&")
				.Replace("&", "&amp;")
				.Replace("<", "&lt;")
				.Replace(">", "&gt;");
		}
		return text;
	}

	internal static List<int> EnumInt(IEnumerator ie)
	{
		List<int> list = new List<int>();
		while (ie.MoveNext())
		{
			if (ie.Current != null)
			{
				list.Add((int)ie.Current);
			}
		}
		return list;
	}

	internal static List<IndexedObject> EnumObj(IEnumerator iE)
	{
		List<IndexedObject> list = new List<IndexedObject>();
		while (iE.MoveNext())
		{
			list.Add((IndexedObject)iE.Current);
		}
		return list;
	}

	internal static List<string> EnumStr(IEnumerator iE)
	{
		List<string> list = new List<string>();
		while (iE.MoveNext())
		{
			list.Add((string)iE.Current);
		}
		return list;
	}

	internal static double BytesToMegabytes(long bytes)
	{
		return (float)bytes / 1024f / 1024f;
	}

	internal static double KilobytesToMegabytes(long kilobytes)
	{
		return (float)kilobytes / 1024f;
	}

	internal static string MostFrequent(IEnumerator inp)
	{
		List<string> source = EnumStr(inp);
		if (source.Any())
		{
			return (from v in source
				group v by v into g
				orderby g.Count() descending
				select g).First().Key;
		}
		return "";
	}

	internal static string RandomString(int lLength)
	{
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < lLength; i++)
		{
			if (Random.Next(0, 2) == 1)
			{
				stringBuilder.Append((char)Random.Next(65, 90));
			}
			else
			{
				stringBuilder.Append((char)Random.Next(97, 122));
			}
		}
		return stringBuilder.ToString();
	}

	internal static bool StringToBool(string sIn)
	{
		return sIn.ToLower().Trim() == "true";
	}

	internal static string BoolToString(bool sIn)
	{
		if (sIn)
		{
			return "True";
		}
		return "False";
	}

	internal static string Repeat(string input, int count)
	{
		StringBuilder stringBuilder = new StringBuilder((input?.Length ?? 0) * count);
		for (int i = 0; i < count; i++)
		{
			stringBuilder.Append(input);
		}
		return stringBuilder.ToString();
	}

	internal static string MakeErr(NNTPError zErr)
	{
		return Convert.ToString(zErr.Code) + " " + zErr.Message;
	}

	internal static string FormatElapsed(TimeSpan ts)
	{
		string text = $"{ts.Days:00}:{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
		if (text.StartsWith("00:"))
		{
			text = text.Substring(3);
		}
		return text;
	}

	internal static string FormatDate(DateTime dt)
	{
		return dt.ToString("HH:mm:ss ddd dd MMM", CultureInfo.CreateSpecificCulture("en-US"));
	}

	internal static string XmlToString(VirtualItem vi)
	{
		StringBuilder stringBuilder = new StringBuilder();
		XmlWriter xR = CreateWriter(stringBuilder);
		if (!vi.WriteXML(xR))
		{
			return "";
		}
		return stringBuilder.ToString();
	}

	internal static XmlWriter CreateWriter(StringBuilder sX)
	{
		XmlWriter xmlWriter = XmlWriter.Create(sX, WriterSettings);
		xmlWriter.WriteProcessingInstruction("xml", "version='1.0' encoding='ISO-8859-1'");
		return xmlWriter;
	}

	internal static NNTPInfo CountInfo(List<VirtualItem> cList)
	{
		long num = 0L;
		long num2 = 0L;
		long num3 = 0L;
		long num4 = 0L;
		foreach (VirtualItem c in cList)
		{
			if (c != null)
			{
				NNTPInfo info = c.Info;
				num2 += info.Total;
				num3 += info.Expected;
				num += info.Available;
				num4 += info.BytesDone;
			}
		}
		return new NNTPInfo(num, num3, num2, num4);
	}

	internal static string TranslateStatus(int lStatus)
	{
		return lStatus switch
		{
			0 => "Queued", 
			1 => "Downloading", 
			2 => "Decoding", 
			3 => "Extracting", 
			4 => "Verifying", 
			5 => "Repairing", 
			6 => "Paused", 
			7 => "Completed", 
			8 => "Failed", 
			_ => "Error", 
		};
	}

	internal static NNTPError TranslateError(SocketError sockErr)
	{
		NNTPError nNTPError = new NNTPError();
		switch (sockErr)
		{
		case SocketError.Success:
			nNTPError.Code = 900;
			nNTPError.Message = "Success.";
			return nNTPError;
		case SocketError.OperationAborted:
			nNTPError.Code = 946;
			nNTPError.Message = "The overlapped operation was aborted due to the closure of the Socket.";
			return nNTPError;
		case SocketError.IOPending:
			nNTPError.Code = 945;
			nNTPError.Message = "The application has initiated an overlapped operation that cannot be completed immediately.";
			return nNTPError;
		case SocketError.Interrupted:
			nNTPError.Code = 902;
			nNTPError.Message = "A blocking Socket call was canceled.";
			return nNTPError;
		case SocketError.AccessDenied:
			nNTPError.Code = 903;
			nNTPError.Message = "An attempt was made to access a Socket in a way that is forbidden by its access permissions.";
			return nNTPError;
		case SocketError.Fault:
			nNTPError.Code = 904;
			nNTPError.Message = "An invalid pointer address was detected by the underlying socket provider.";
			return nNTPError;
		case SocketError.InvalidArgument:
			nNTPError.Code = 905;
			nNTPError.Message = "An invalid argument was supplied to a Socket member.";
			return nNTPError;
		case SocketError.TooManyOpenSockets:
			nNTPError.Code = 906;
			nNTPError.Message = "There are too many open sockets in the underlying socket provider.";
			return nNTPError;
		case SocketError.WouldBlock:
			nNTPError.Code = 907;
			nNTPError.Message = "An operation on a nonblocking socket cannot be completed immediately.";
			return nNTPError;
		case SocketError.InProgress:
			nNTPError.Code = 908;
			nNTPError.Message = "A blocking operation is in progress.";
			return nNTPError;
		case SocketError.AlreadyInProgress:
			nNTPError.Code = 909;
			nNTPError.Message = "The nonblocking Socket already has an operation in progress.";
			return nNTPError;
		case SocketError.NotSocket:
			nNTPError.Code = 910;
			nNTPError.Message = "A Socket operation was attempted on a non-socket.";
			return nNTPError;
		case SocketError.DestinationAddressRequired:
			nNTPError.Code = 911;
			nNTPError.Message = "A required address was omitted from an operation on a Socket.";
			return nNTPError;
		case SocketError.MessageSize:
			nNTPError.Code = 912;
			nNTPError.Message = "The datagram is too long.";
			return nNTPError;
		case SocketError.ProtocolType:
			nNTPError.Code = 913;
			nNTPError.Message = "The protocol type is incorrect for this Socket.";
			return nNTPError;
		case SocketError.ProtocolOption:
			nNTPError.Code = 914;
			nNTPError.Message = "An unknown, invalid, or unsupported option or level was used with a Socket.";
			return nNTPError;
		case SocketError.ProtocolNotSupported:
			nNTPError.Code = 915;
			nNTPError.Message = "The protocol is not implemented or has not been configured.";
			return nNTPError;
		case SocketError.SocketNotSupported:
			nNTPError.Code = 916;
			nNTPError.Message = "The support for the specified socket type does not exist in this address family.";
			return nNTPError;
		case SocketError.OperationNotSupported:
			nNTPError.Code = 917;
			nNTPError.Message = "The address family is not supported by the protocol family.";
			return nNTPError;
		case SocketError.ProtocolFamilyNotSupported:
			nNTPError.Code = 918;
			nNTPError.Message = "The protocol family is not implemented or has not been configured.";
			return nNTPError;
		case SocketError.AddressFamilyNotSupported:
			nNTPError.Code = 919;
			nNTPError.Message = "The address family specified is not supported. This error is returned if the IPv6 address family was specified and the IPv6 stack is not installed on the local machine. This error is returned if the IPv4 address family was specified and the IPv4 stack is not installed on the local machine.";
			return nNTPError;
		case SocketError.AddressAlreadyInUse:
			nNTPError.Code = 920;
			nNTPError.Message = "Only one use of an address is normally permitted.";
			return nNTPError;
		case SocketError.AddressNotAvailable:
			nNTPError.Code = 921;
			nNTPError.Message = "The selected IP address is not valid in this context.";
			return nNTPError;
		case SocketError.NetworkDown:
			nNTPError.Code = 922;
			nNTPError.Message = "The network is not available.";
			return nNTPError;
		case SocketError.NetworkUnreachable:
			nNTPError.Code = 923;
			nNTPError.Message = "No route to the remote host exists.";
			return nNTPError;
		case SocketError.NetworkReset:
			nNTPError.Code = 924;
			nNTPError.Message = "The application tried to set KeepAlive on a connection that has already timed out.";
			return nNTPError;
		case SocketError.ConnectionAborted:
			nNTPError.Code = 925;
			nNTPError.Message = "The connection was aborted by the .NET Framework or the underlying socket provider.";
			return nNTPError;
		case SocketError.ConnectionReset:
			nNTPError.Code = 926;
			nNTPError.Message = "The connection was reset by the remote peer.";
			return nNTPError;
		case SocketError.NoBufferSpaceAvailable:
			nNTPError.Code = 927;
			nNTPError.Message = "No free buffer space is available for a Socket operation.";
			return nNTPError;
		case SocketError.IsConnected:
			nNTPError.Code = 928;
			nNTPError.Message = "The Socket is already connected.";
			return nNTPError;
		case SocketError.NotConnected:
			nNTPError.Code = 929;
			nNTPError.Message = "The application tried to send or receive data, and the Socket is not connected.";
			return nNTPError;
		case SocketError.Shutdown:
			nNTPError.Code = 930;
			nNTPError.Message = "A request to send or receive data was disallowed because the Socket has already been closed.";
			return nNTPError;
		case SocketError.TimedOut:
			nNTPError.Code = 931;
			nNTPError.Message = "The connection attempt timed out, or the connected host has failed to respond.";
			return nNTPError;
		case SocketError.ConnectionRefused:
			nNTPError.Code = 932;
			nNTPError.Message = "The remote host is actively refusing a connection.";
			return nNTPError;
		case SocketError.HostDown:
			nNTPError.Code = 933;
			nNTPError.Message = "The operation failed because the remote host is down.";
			return nNTPError;
		case SocketError.HostUnreachable:
			nNTPError.Code = 934;
			nNTPError.Message = "There is no network route to the specified host.";
			return nNTPError;
		case SocketError.ProcessLimit:
			nNTPError.Code = 935;
			nNTPError.Message = "Too many processes are using the underlying socket provider.";
			return nNTPError;
		case SocketError.SystemNotReady:
			nNTPError.Code = 936;
			nNTPError.Message = "The network subsystem is unavailable.";
			return nNTPError;
		case SocketError.VersionNotSupported:
			nNTPError.Code = 937;
			nNTPError.Message = "The version of the underlying socket provider is out of range.";
			return nNTPError;
		case SocketError.NotInitialized:
			nNTPError.Code = 938;
			nNTPError.Message = "The underlying socket provider has not been initialized.";
			return nNTPError;
		case SocketError.Disconnecting:
			nNTPError.Code = 939;
			nNTPError.Message = "A graceful shutdown is in progress.";
			return nNTPError;
		case SocketError.TypeNotFound:
			nNTPError.Code = 940;
			nNTPError.Message = "The specified class was not found.";
			return nNTPError;
		case SocketError.HostNotFound:
			nNTPError.Code = 941;
			nNTPError.Message = "Could not connect to the internet. Please check your internet connection is working properly.";
			return nNTPError;
		case SocketError.TryAgain:
			nNTPError.Code = 942;
			nNTPError.Message = "The name of the host could not be resolved. Try again later.";
			return nNTPError;
		case SocketError.NoRecovery:
			nNTPError.Code = 943;
			nNTPError.Message = "The error is unrecoverable or the requested database cannot be located.";
			return nNTPError;
		case SocketError.NoData:
			nNTPError.Code = 944;
			nNTPError.Message = "The requested name or IP address was not found on the name server.";
			return nNTPError;
		default:
			nNTPError.Code = 901;
			nNTPError.Message = "An unspecified Socket error has occurred.";
			return nNTPError;
		}
	}

	internal static string CleanString(string sIn)
	{
		if (sIn == null)
		{
			return null;
		}
		byte[] bytes = CEnc.GetBytes(sIn);
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < bytes.Length; i++)
		{
			if (bytes[i] >= 32 && bytes[i] < 127)
			{
				stringBuilder.Append((char)bytes[i]);
			}
			else
			{
				stringBuilder.Append("?");
			}
		}
		return stringBuilder.ToString();
	}
}
