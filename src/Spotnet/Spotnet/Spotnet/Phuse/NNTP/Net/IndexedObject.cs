namespace Spotnet.Phuse.NNTP.Net;

internal interface IndexedObject
{
	int ID { get; set; }

	int Index { get; set; }

	int CompareTo(object obj);

	int CompareTo(IndexedObject obj);
}
