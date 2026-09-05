using System;

namespace Spotnet.Model;

[Serializable]
public class UserInfo
{
	public string Avatar = "";

	public string Modulus = "";

	public string Organisation = "";

	public string Signature = "";

	public string Trace = "";

	public bool ValidSignature;
}
