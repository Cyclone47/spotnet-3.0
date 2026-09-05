using Microsoft.VisualBasic;

namespace Spotnet.Model;

internal class SpotCat
{
	public Collection Children;

	public string Name;

	public string Tag;

	public SpotCat()
	{
		Children = new Collection();
	}

	public bool AddChild(string sName)
	{
		Children.Add(sName);
		return true;
	}
}
