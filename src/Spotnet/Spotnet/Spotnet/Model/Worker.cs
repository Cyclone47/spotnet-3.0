using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Model;

internal class Worker
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public string HeaderData;

	public int InstanceCount;

	internal RSACryptoServiceProvider[] Rsa;

	public NntpSettings XSettings;

	private List<Spot> _xOutputData;

	private static SpotEx CopyToEx(Spot sX)
	{
		return new SpotEx
		{
			Category = sX.Category,
			Filesize = sX.Filesize,
			KeyID = sX.KeyID,
			MessageId = sX.MessageId,
			Poster = sX.Poster,
			Stamp = sX.Stamp,
			SubCat = sX.SubCat,
			SubCats = sX.SubCats,
			Tag = sX.Tag,
			Title = sX.Title,
			Article = sX.Article,
			Modulus = sX.Modulus
		};
	}

	private bool DoWork(ref int sTotal, ref int xCnt, ref int xCntNew, bool skipBacklistedSpots)
	{
		string[] array = HeaderData.Split('\n');
		if (array.Length == 3)
		{
			return true;
		}
		// SHA1.Create() over the platform implementation. SHA1Managed is the .NET Framework
		// managed hash that a FIPS-policy machine refused to construct; the digest is identical,
		// so spot signature verification is unchanged.
		using SHA1 managed = SHA1.Create();
		List<string> list = new List<string>();
		int num = (int)Math.Round((DateTime.UtcNow - SpotHelper.Epoch).TotalSeconds) + 25000;
		_xOutputData = new List<Spot>();
		sTotal = array.Length - 3;
		int num2 = 0;
		for (int num3 = sTotal; num3 > 0; num3--)
		{
			try
			{
				string text = array[num3].Trim();
				string[] array2 = text.Split('\t');
				string text2;
				string text3;
				string text5;
				Spot spot;
				int num4;
				int num6;
				string text6;
				string[] array3;
				bool flag;
				if (array2.Length >= 5)
				{
					string s = array2[0];
					text2 = array2[1];
					text3 = array2[2];
					string text4 = array2[3];
					text5 = array2[4];
					num2 = 1;
					if (text.Length >= 5 && !text2.IsNullOrEmpty())
					{
						spot = new Spot
						{
							MessageId = text5.Substring(1, text5.Length - 2)
						};
						num4 = text3.IndexOf("@", StringComparison.Ordinal);
						if (num4 >= 1)
						{
							num2 = 2;
							int num5 = text3.IndexOf(">", StringComparison.Ordinal);
							num6 = text3.IndexOf("<", StringComparison.Ordinal);
							if (num6 >= 1 && num6 <= num4 && num5 >= num4 && num4 <= text3.Length - 2)
							{
								text6 = text3.Substring(num4 + 1, num5 - num4 - 1);
								array3 = text6.Split('.');
								if (array3.Length >= 7 && array3[0].Length >= 2 && byte.TryParse(array3[0].Substring(1, 1), out spot.KeyID) && spot.KeyID >= 1)
								{
									num2 = 3;
									flag = spot.KeyID != 1;
									if (long.TryParse(s, out spot.Article) && spot.Article >= 1 && long.TryParse(array3[3], out spot.Stamp) && spot.Stamp >= 1218171600 && (flag || (int.TryParse(array3[3], out var result) && result >= 1000000 && spot.Stamp <= 1317617563 && text4.Contains("2010"))))
									{
										if (spot.Stamp > num)
										{
											spot.Stamp = num - 25000;
										}
										if (!long.TryParse(array3[1], out spot.Filesize))
										{
											spot.Filesize = 0L;
											goto IL_02a3;
										}
										if (spot.Filesize < 0)
										{
											spot.Filesize = 0L;
										}
										if (spot.Filesize != 94165742)
										{
											goto IL_02a3;
										}
									}
								}
							}
						}
					}
				}
				goto end_IL_0076;
				IL_02a3:
				num2 = 4;
				spot.Poster = text3.Substring(0, num6).Trim();
				string text7;
				if (int.TryParse(array3[0].Substring(0, 1), out spot.Category) && spot.Category >= 1)
				{
					spot.SubCat = 100;
					text7 = "";
					string text8 = array3[0].Substring(2).ToLower();
					if (!flag)
					{
						list.Clear();
						char[] array4 = text8.ToCharArray();
						for (int j = 0; j < text8.Length; j++)
						{
							if (Conversion.Val(array4[j]) == 0 && Conversions.ToString(array4[j]) != "0" && text7.Length > 0)
							{
								list.Add(text7);
								text7 = null;
							}
							text7 += text8[j];
						}
						list.Add(text7);
						text7 = "";
						foreach (string item in list)
						{
							text7 = text7 + item[0] + Conversions.ToString(Conversions.ToByte(item.Substring(1))) + "|";
						}
						spot.SubCat = Conversions.ToByte(list[0].Substring(1));
						goto IL_04d4;
					}
					if (text8.Length >= 3 && text8.Length % 3 == 0)
					{
						foreach (string item2 in from str in SpotHelper.SplitBySizEx(text8, 3)
							where str.Length > 0
							select str)
						{
							int num7 = Strings.Asc(item2);
							if (num7 > 96 && num7 < 123 && byte.TryParse(item2.Substring(1), out var result2))
							{
								text7 = text7 + Strings.Chr(num7) + Conversions.ToString(result2) + "|";
								if (num7 == 97)
								{
									spot.SubCat = result2;
								}
							}
						}
						goto IL_04d4;
					}
				}
				goto end_IL_0076;
				IL_04d4:
				num2 = 5;
				string userSignature;
				string text9;
				string text10;
				if (spot.SubCat <= 99 && text7.Length >= 3)
				{
					spot.SubCats = text7;
					if (spot.Category == 1)
					{
						if (SpotHelper.IsTv(spot.SubCats))
						{
							spot.Category = 6;
						}
						else if (SpotHelper.IsEro(spot.SubCats))
						{
							spot.Category = 9;
						}
						else if (SpotHelper.IsEbook(spot.SubCats))
						{
							spot.Category = 5;
						}
					}
					num2 = 6;
					if (text2.Contains("=?") && text2.Contains("?="))
					{
						text2 = SpotHelper.Parse(text2.Trim().Replace("?= =?", "?==?"));
					}
					if (text2.Contains("|"))
					{
						string[] array5 = Strings.Split(text2, "|");
						spot.Title = array5[0].Trim();
						spot.Tag = array5[array5.Length - 1].Trim();
					}
					else
					{
						spot.Title = text2.Trim();
						spot.Tag = null;
					}
					num2 = 7;
					if (spot.Title.Contains(Conversions.ToString(Strings.Chr(194))) || spot.Title.Contains(Conversions.ToString(Strings.Chr(195))))
					{
						if (flag)
						{
							spot.Title = spot.Title.Replace(Conversions.ToString(Strings.Chr(194)), "?").Replace(Conversions.ToString(Strings.Chr(195)), "?");
						}
						else
						{
							spot.Title = SpotHelper.Parse(SpotHelper.UtfEncode(spot.Title)).Trim();
						}
					}
					if (spot.Title.Length != 0 && spot.Poster.Length != 0)
					{
						num2 = 8;
						userSignature = "";
						text9 = text3.Substring(num6 + 1, num4 - num6 - 1);
						if (text9.Length > 50)
						{
							int num8 = text9.IndexOf(".", StringComparison.Ordinal);
							if (num8 == -1)
							{
								text9 = SpotHelper.FixPadding(SpotHelper.UnSpecialString(text9));
							}
							else
							{
								userSignature = SpotHelper.FixPadding(SpotHelper.UnSpecialString(text9.Substring(num8 + 1)));
								text9 = SpotHelper.FixPadding(SpotHelper.UnSpecialString(text9.Substring(0, num8)));
							}
						}
						num2 = 9;
						if (!skipBacklistedSpots || XSettings.BlackList.Count <= 0 || !XSettings.BlackList.Contains(text9) || text9.Equals(UserKeyHelper.GetModulus()))
						{
							if (spot.KeyID == 1)
							{
								_xOutputData.Add(spot);
							}
							else if (!XSettings.CheckSignatures && spot.KeyID != 2)
							{
								_xOutputData.Add(spot);
							}
							else
							{
								num2 = 10;
								text10 = array3.Last();
								if (!text10.IsNullOrEmpty())
								{
									if (spot.KeyID != 7)
									{
										if (spot.KeyID != 2 || spot.Filesize != 999 || text9.Length < 50 || !VerifySignOfSpotnetSpot(spot, text9, userSignature, text6, text10, text5, managed))
										{
											goto IL_089d;
										}
										string value = text5.Split('.')[0].Substring(1);
										string[] array6 = spot.Title.Split();
										if (array6.Length >= 2 && array6[1].Length >= 3)
										{
											if (!SpotHelper.MakeMsg(array6[1], tag: false).Split('@')[0].Equals(value))
											{
												goto IL_089d;
											}
											spot.IsSpotnetDisposeReportFromAuthorOfSpot = true;
											_xOutputData.Add(spot);
										}
									}
									else if (Rsa[7] == null && VerifySignOfSpotnetSpot(spot, text9, userSignature, text6, text10, text5, managed))
									{
										goto IL_08e9;
									}
								}
							}
						}
					}
				}
				goto end_IL_0076;
				IL_08e9:
				num2 = 16;
				_xOutputData.Add(spot);
				goto end_IL_0076;
				IL_089d:
				if (Rsa[spot.KeyID] != null && VerifySignOfSpotFromKeysFile(spot, text9, userSignature, text6, text10, text5, managed))
				{
					goto IL_08e9;
				}
				end_IL_0076:;
			}
			catch (Exception ex)
			{
				Log.Debug("On step #" + num2);
				Log.Exception(ex);
			}
		}
		xCnt += _xOutputData.Count;
		xCntNew = _xOutputData.AsParallel().Count((Spot i) => !i.IsMarkedAsDisposeReport(out var _));
		_xOutputData.Reverse();
		return true;
	}

	private bool VerifySignOfSpotnetSpot(Spot spot, string modulus, string userSignature, string posterAfterTheDog, string posterAfterTheDogLastPart, string msgId, SHA1 managed)
	{
		byte[] array = managed.ComputeHash(SpotHelper.MakeLatin(msgId));
		if (array[0] != 0 || array[1] != 0)
		{
			return false;
		}
		if (modulus.Length < 50)
		{
			spot.KeyID = 9;
		}
		else
		{
			RSACryptoServiceProvider rSACryptoServiceProvider = SpotHelper.MakeRsa(modulus);
			if (rSACryptoServiceProvider == null)
			{
				return false;
			}
			if (userSignature.Length > 0)
			{
				if (!rSACryptoServiceProvider.VerifyHash(array, null, Convert.FromBase64String(userSignature)))
				{
					return false;
				}
				spot.Modulus = modulus;
			}
			else
			{
				if (!rSACryptoServiceProvider.VerifyHash(managed.ComputeHash(SpotHelper.MakeLatin(spot.Title + posterAfterTheDog.Substring(0, posterAfterTheDog.Length - posterAfterTheDogLastPart.Length - 1) + spot.Poster)), null, Convert.FromBase64String(SpotHelper.FixPadding(SpotHelper.UnSpecialString(posterAfterTheDogLastPart)))))
				{
					return false;
				}
				spot.Modulus = modulus;
			}
		}
		return true;
	}

	private bool VerifySignOfSpotFromKeysFile(Spot spot, string modulus, string userSignature, string posterAfterTheDog, string posterAfterTheDogLastPart, string msgId, SHA1 managed)
	{
		byte[] array = null;
		if (userSignature.Length > 0)
		{
			array = managed.ComputeHash(SpotHelper.MakeLatin(msgId));
			if (array[0] != 0 || array[1] != 0)
			{
				return false;
			}
		}
		byte[] rgbSignature;
		try
		{
			rgbSignature = Convert.FromBase64String(SpotHelper.FixPadding(SpotHelper.UnSpecialString(posterAfterTheDogLastPart)));
		}
		catch (FormatException ex)
		{
			Log.Exception(ex);
			return false;
		}
		if (!Rsa[spot.KeyID].VerifyHash(managed.ComputeHash(SpotHelper.MakeLatin(spot.Title + posterAfterTheDog.Substring(0, posterAfterTheDog.Length - posterAfterTheDogLastPart.Length - 1) + spot.Poster)), null, rgbSignature))
		{
			return false;
		}
		if (userSignature.Length > 0)
		{
			RSACryptoServiceProvider rSACryptoServiceProvider = SpotHelper.MakeRsa(modulus);
			if (rSACryptoServiceProvider == null || !rSACryptoServiceProvider.VerifyHash(array, null, Convert.FromBase64String(userSignature)))
			{
				return false;
			}
			spot.Modulus = modulus;
		}
		return true;
	}

	internal bool ParseHeaders(Func<bool, int, int, string, List<Spot>, long, bool, bool> workDoneAction)
	{
		int xCnt = 0;
		int sTotal = 0;
		int xCntNew = 0;
		string arg = "";
		bool flag;
		try
		{
			flag = DoWork(ref sTotal, ref xCnt, ref xCntNew, Settings.Default.HideBlacklistedSpots);
		}
		catch (Exception ex)
		{
			flag = false;
			arg = ex.Message;
		}
		return workDoneAction(!flag, 0, InstanceCount, arg, _xOutputData, xCntNew, arg7: false);
	}

	internal SpotEx ParseSpot(string sSubject, string sFrom, string sMessageId, NntpSettings xParam)
	{
		string[] array = new string[5];
		SpotEx result;
		try
		{
			array[0] = "1234";
			array[1] = sSubject;
			array[2] = sFrom;
			array[3] = "2010";
			array[4] = sMessageId;
			InstanceCount = 1;
			HeaderData = string.Format("\r\n{0}\r\n.\r\n", Strings.Join(array, "\t"));
			XSettings = xParam;
			XSettings.CheckSignatures = true;
			Rsa = SpotHelper.GetRsa(xParam.TrustedKeys);
			int sTotal = 1;
			int xCnt = 1;
			int xCntNew = 0;
			if (!DoWork(ref sTotal, ref xCnt, ref xCntNew, skipBacklistedSpots: false))
			{
				return null;
			}
			if (_xOutputData.Count == 0)
			{
				Log.Error(Words.SignatureInvalid + ". MsgId: " + sMessageId);
				return null;
			}
			result = CopyToEx(_xOutputData[0]);
		}
		catch (Exception ex)
		{
			result = null;
			Log.Exception(ex);
		}
		return result;
	}

	internal SpotEx ParseSpotXML(ref SpotEx lz, XmlDocument theDoc, string xmlSignature, bool bCheck)
	{
		SpotEx result2;
		try
		{
			IEnumerator enumerator = null;
			XmlNode xmlNode = (theDoc.SelectSingleNode("Spotnet") ?? theDoc.SelectSingleNode("SpotNet")).SelectSingleNode("Posting");
			bool flag;
			try
			{
				flag = theDoc.GetElementsByTagName("ID").Count == 0;
			}
			catch (Exception)
			{
				flag = true;
			}
			if (!flag)
			{
				try
				{
					flag = Conversions.ToByte(((XmlElement)theDoc.GetElementsByTagName("Key")[0]).InnerText) != 1;
				}
				catch (Exception)
				{
				}
			}
			if (flag)
			{
				lz.OldInfo = null;
			}
			XmlNode xmlNode2 = xmlNode.SelectSingleNode("Description");
			if (xmlNode2 == null)
			{
				Log.Error("E1");
				return null;
			}
			lz.Body = SpotHelper.HtmlDecode(xmlNode2.InnerText.Trim());
			if (xmlNode.SelectSingleNode("Image") != null)
			{
				lz.Image = SpotHelper.HtmlDecode(xmlNode.SelectSingleNode("Image").InnerText);
				if (flag)
				{
					XmlNode xmlNode3 = xmlNode.SelectSingleNode("Image");
					if (xmlNode3.Attributes["Width"] != null)
					{
						lz.ImageWidth = (int)Math.Round(Conversion.Val(xmlNode3.Attributes["Width"].InnerText));
					}
					if (xmlNode3.Attributes["Height"] != null)
					{
						lz.ImageHeight = (int)Math.Round(Conversion.Val(xmlNode3.Attributes["Height"].InnerText));
					}
					try
					{
						enumerator = xmlNode3.SelectNodes("Segment").GetEnumerator();
						if (enumerator.MoveNext())
						{
							XmlNode xmlNode4 = (XmlNode)enumerator.Current;
							lz.ImageID = SpotHelper.MakeMsg(Strings.Trim(xmlNode4.InnerText));
							lz.Image = "";
						}
					}
					finally
					{
						if (enumerator is IDisposable)
						{
							(enumerator as IDisposable).Dispose();
						}
					}
				}
			}
			if (xmlNode.SelectSingleNode("Website") != null)
			{
				lz.Web = SpotHelper.HtmlDecode(xmlNode.SelectSingleNode("Website").InnerText);
			}
			if (!flag)
			{
				IEnumerator enumerator2 = null;
				if (xmlNode.SelectSingleNode("Filename") != null)
				{
					lz.OldInfo.FileName = xmlNode.SelectSingleNode("Filename").InnerText;
				}
				try
				{
					enumerator2 = xmlNode.SelectNodes("Newsgroup").GetEnumerator();
					while (enumerator2.MoveNext())
					{
						XmlNode xmlNode5 = (XmlNode)enumerator2.Current;
						if (xmlNode5.InnerText != "Other")
						{
							FTDInfo oldInfo = lz.OldInfo;
							oldInfo.Groups = oldInfo.Groups + xmlNode5.InnerText + "|";
						}
					}
				}
				finally
				{
					if (enumerator2 is IDisposable)
					{
						(enumerator2 as IDisposable).Dispose();
					}
				}
			}
			else
			{
				try
				{
					XmlNode xmlNode6 = xmlNode.SelectSingleNode("NZR");
					if (xmlNode6 != null && xmlNode6.Attributes != null)
					{
						XmlNode namedItem = xmlNode6.Attributes.GetNamedItem("K");
						if (namedItem != null)
						{
							if (!int.TryParse(namedItem.InnerText, out var result))
							{
								Log.Debug("NZB key cannot be parsed");
							}
							else
							{
								string text = NzrDecoder.Decode(xmlNode6.InnerText, result);
								if (!text.IsNullOrEmpty())
								{
									xmlNode6.RemoveChild(xmlNode6.FirstChild);
									XmlDocumentFragment xmlDocumentFragment = theDoc.CreateDocumentFragment();
									xmlDocumentFragment.InnerXml = text;
									xmlNode6.AppendChild(xmlDocumentFragment);
									lz.NZR = ParseNzbNode(xmlNode6);
									lz.NZRKey = result;
								}
							}
						}
					}
				}
				catch (Exception ex3)
				{
					Log.Debug("NZR parse error: " + ex3.Message);
				}
				XmlNode xmlNode7 = xmlNode.SelectSingleNode("NZB");
				if (xmlNode7 == null)
				{
					Log.Error("E3");
					return null;
				}
				lz.NZB = ParseNzbNode(xmlNode7);
				if (lz.NZB.IsNullOrEmpty())
				{
					Log.Error("E4");
					return null;
				}
			}
			lz.User.ValidSignature = false;
			if (!bCheck || lz.KeyID == 1)
			{
				return lz;
			}
			if (Strings.Len(lz.User.Signature) > 0 && Strings.Len(lz.User.Modulus) > 0 && Strings.Len(lz.MessageId) > 0)
			{
				lz.User.ValidSignature = SpotHelper.CheckUserSignature(SpotHelper.MakeMsg(lz.MessageId), lz.User.Signature, lz.User.Modulus);
			}
			if (!lz.User.ValidSignature && Strings.Len(lz.User.Signature) > 0 && Strings.Len(lz.User.Modulus) > 0 && Strings.Len(xmlSignature) > 0)
			{
				lz.User.ValidSignature = SpotHelper.CheckUserSignature(xmlSignature, lz.User.Signature, lz.User.Modulus);
			}
			if (!lz.User.ValidSignature && (lz.KeyID != 3 || lz.Stamp > 1317617563 || Strings.Len(lz.User.Modulus) > 0))
			{
				Log.Error(Words.SignatureInvalid + ". MsgId: " + lz.MessageId);
				return null;
			}
			result2 = lz;
		}
		catch (Exception ex4)
		{
			result2 = null;
			Log.Exception(ex4);
		}
		return result2;
	}

	private string ParseNzbNode(XmlNode nzb)
	{
		try
		{
			IEnumerator enumerator = null;
			string text = "";
			try
			{
				enumerator = nzb.SelectNodes("Segment").GetEnumerator();
				while (enumerator.MoveNext())
				{
					XmlNode xmlNode = (XmlNode)enumerator.Current;
					text = text + xmlNode.InnerText + " ";
				}
			}
			finally
			{
				if (enumerator is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}
			return text.Trim().Replace("\"", "");
		}
		catch (Exception)
		{
			return null;
		}
	}
}
