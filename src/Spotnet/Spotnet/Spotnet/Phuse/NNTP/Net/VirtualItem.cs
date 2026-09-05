using System.Collections.Generic;
using System.Xml;

namespace Spotnet.Phuse.NNTP.Net;

internal interface VirtualItem
{
	int Count { get; }

	NNTPInfo Info { get; }

	List<VirtualItem> VirtualList { get; }

	bool WriteXML(XmlWriter xR);
}
