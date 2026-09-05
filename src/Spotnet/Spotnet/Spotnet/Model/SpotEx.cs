using System;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.ViewModel;

namespace Spotnet.Model;

[Serializable]
public class SpotEx : Spot
{
	public string Body = "";

	public byte[] ImageSource;

	public string Image = "";

	public string PreviewImage = "";

	public int ImageHeight;

	public string ImageID = "";

	public int ImageWidth;

	public string NZB = "";

	public string NZR = "";

	public int NZRKey = -1;

	public FTDInfo OldInfo = new FTDInfo();

	public UserInfo User = new UserInfo();

	public string Web = "";

	public bool IsPreview;

	public bool DoNotLoadImageAutomatically;

	public string Newsreader;

	private PosterIdentType _posterIdent;

	public PosterIdentType PosterIdent
	{
		get
		{
			if (_posterIdent == PosterIdentType.Unspecified)
			{
				if (!Modulus.IsNullOrEmpty() && !Poster.IsNullOrEmpty() && !Modulus.Equals("none"))
				{
					if (BlackAndWhite.BlackList().Contains(Modulus))
					{
						_posterIdent = PosterIdentType.Black;
					}
					else if (BlackAndWhite.SpotBlackList().Contains(MessageId))
					{
						_posterIdent = PosterIdentType.SpotBlack;
					}
					else if (BlackAndWhite.WhiteList().Contains(Modulus))
					{
						_posterIdent = PosterIdentType.White;
					}
					else if (Stamp > 0 && AppHelper.Epoch.AddSeconds(Stamp) < DateTime.Parse("2013-01-01 00:00:00Z"))
					{
						PosterIdent = PosterIdentType.Verified;
					}
					else if (BlackAndWhite.IsModulusInServerWhitelist(Modulus))
					{
						_posterIdent = PosterIdentType.Verified;
					}
					else if (BlackAndWhite.SpotWhiteList().Contains(MessageId))
					{
						_posterIdent = PosterIdentType.SpotWhite;
					}
					else if (BlackAndWhite.IsUsernameInServerWhitelist(Poster))
					{
						_posterIdent = PosterIdentType.Fake;
					}
					else
					{
						_posterIdent = PosterIdentType.None;
					}
				}
				else if (Stamp > 0 && AppHelper.Epoch.AddSeconds(Stamp) < DateTime.Parse("2013-01-01 00:00:00Z"))
				{
					PosterIdent = PosterIdentType.Verified;
				}
				else
				{
					PosterIdent = PosterIdentType.None;
				}
			}
			return _posterIdent;
		}
		set
		{
			_posterIdent = value;
		}
	}

	public void SaveToCache()
	{
		FileCacheManager.Save(this);
	}

	public SpotEx ShallowCopy()
	{
		return (SpotEx)MemberwiseClone();
	}
}
