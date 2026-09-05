namespace Spotnet.Helpers;

internal static class ThumbsUploader
{
	internal static string GetThumbMessageId(string spotMsgId)
	{
		string text = SpotHelper.MakeMsg(spotMsgId, tag: false);
		string arg = AppHelper.MakeMd5(text + "sup.secure").Substring(2, 10);
		return $"{arg}.{text}";
	}
}
