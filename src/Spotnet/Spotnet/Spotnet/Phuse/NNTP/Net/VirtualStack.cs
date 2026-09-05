namespace Spotnet.Phuse.NNTP.Net;

internal class VirtualStack : IndexedObject
{
	private readonly IndexedCollection zCol = new IndexedCollection();

	public int ID { get; set; }

	public int Index { get; set; }

	public int CompareTo(object obj)
	{
		return CompareTo(obj as IndexedObject);
	}

	public int CompareTo(IndexedObject obj)
	{
		return Index.CompareTo(obj.Index);
	}

	public IndexedCollection Stack(int SlotID)
	{
		if (!zCol.ContainsKey(SlotID))
		{
			zCol.Add(SlotID, new IndexedCollection());
		}
		return (IndexedCollection)zCol.Item(SlotID);
	}
}
