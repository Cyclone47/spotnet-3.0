namespace Spotnet.Model;

public class ListItem
{
	public string Key;

	public string Name;

	internal ListItem(string sKey, string sName)
	{
		Key = sKey ?? "";
		Name = sName ?? "";
	}
}
