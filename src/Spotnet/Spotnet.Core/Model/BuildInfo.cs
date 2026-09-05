using System.Xml;

namespace Spotnet.Model;

public class BuildInfo
{
	public string Name { get; set; }

	public string Version { get; set; }

	public string Link { get; set; }

	public string Sum { get; set; }

	public BuildInfo(XmlDocument xml)
	{
		XmlNode xmlNode = xml.GetElementsByTagName("BuildInfo")[0];
		Name = xmlNode.SelectSingleNode("Name").InnerText;
		Version = xmlNode.SelectSingleNode("Version").InnerText;
		Link = xmlNode.SelectSingleNode("Link").InnerText;
		Sum = xmlNode.SelectSingleNode("Sum").InnerText;
	}
}
