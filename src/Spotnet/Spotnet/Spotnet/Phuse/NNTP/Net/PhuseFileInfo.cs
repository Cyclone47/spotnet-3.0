namespace Spotnet.Phuse.NNTP.Net;

internal class PhuseFileInfo
{
	internal int Filesize { get; private set; }

	internal string Filename { get; private set; }

	internal PhuseFileInfo(string filename, int size)
	{
		Filesize = size;
		Filename = filename;
	}
}
