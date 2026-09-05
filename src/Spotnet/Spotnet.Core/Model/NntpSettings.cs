using System.Collections.Generic;

namespace Spotnet.Model;

public class NntpSettings
{
	public HashSet<string> BlackList;

	public bool CheckSignatures;

	public string GroupName;

	public IdPosition Position;

	public string[] TrustedKeys;

	public HashSet<string> WhiteList;
}
