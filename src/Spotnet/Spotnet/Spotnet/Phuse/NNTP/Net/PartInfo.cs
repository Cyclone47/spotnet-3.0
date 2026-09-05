namespace Spotnet.Phuse.NNTP.Net;

internal class PartInfo
{
	private int zEnd;

	private int zBegin;

	internal int Begin => zBegin;

	internal int End => zEnd;

	internal PartInfo(int Begin, int End)
	{
		zBegin = Begin;
		zEnd = End;
	}
}
