namespace Spotnet.Phuse.NNTP.Net;

internal interface IArticleDecoder
{
	bool DecodeBytes(byte[] data);
}
