using System;
using System.Collections.Generic;

using NLog;
using Spotnet.Extensions;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.Helpers;

internal static class FileCacheManager
{
	private static readonly Logger Log;

	private static readonly JsonSpotCache FileCache;

	internal static KeyValuePair<string, SpotEx> PreviewData;

	static FileCacheManager()
	{
		Log = LogManager.GetCurrentClassLogger();
		FileCache = new JsonSpotCache(AppHelper.SettingsFolder);
	}

	public static bool Contains(string messageId)
	{
		if (!(SpotsSourceEnum.FileCache | SpotsSourceEnum.ImageFromFullImagesGroup | SpotsSourceEnum.ImageByUrl | SpotsSourceEnum.ImageFromThumbsGroup).HasFlag(SpotsSourceEnum.FileCache))
		{
			return false;
		}
		if (messageId.IsNullOrEmpty())
		{
			throw new ArgumentNullException("messageId");
		}
		return FileCache.Get(messageId) != null;
	}

	public static SpotEx Get(string messageId)
	{
		try
		{
			if (messageId.Equals(PreviewData.Key))
			{
				return PreviewData.Value;
			}
			if (!(SpotsSourceEnum.FileCache | SpotsSourceEnum.ImageFromFullImagesGroup | SpotsSourceEnum.ImageByUrl | SpotsSourceEnum.ImageFromThumbsGroup).HasFlag(SpotsSourceEnum.FileCache))
			{
				return null;
			}
			if (messageId.IsNullOrEmpty())
			{
				throw new ArgumentNullException("messageId");
			}
			return FileCache.Get(messageId);
		}
		catch (Exception ex)
		{
			Log.Debug("Failed to get from cache: " + ex.Message);
			return null;
		}
	}

	public static void Save(SpotEx spotEx, byte[] image = null)
	{
		if (!(SpotsSourceEnum.FileCache | SpotsSourceEnum.ImageFromFullImagesGroup | SpotsSourceEnum.ImageByUrl | SpotsSourceEnum.ImageFromThumbsGroup).HasFlag(SpotsSourceEnum.FileCache))
		{
			return;
		}
		if (spotEx == null)
		{
			throw new ArgumentNullException("spotEx");
		}
		if (spotEx.MessageId.IsNullOrEmpty())
		{
			throw new ArgumentException("spotEx message id is null");
		}
		SpotEx spotEx2 = spotEx.ShallowCopy();
		spotEx2.PosterIdent = PosterIdentType.Unspecified;
		byte[] imageBytes = (image.IsNullOrEmpty() ? spotEx.ImageSource : image);
		try
		{
			spotEx2.ImageSource = ImageHelper.ImageResize(imageBytes, 143, 210);
		}
		catch (Exception ex)
		{
			Log.Debug(ex.Message);
			spotEx2.ImageSource = null;
		}
		if (spotEx2.Body.IsNullOrEmpty() || spotEx2.ImageSource.IsNullOrEmpty())
		{
			SpotEx spotEx3 = FileCache.Get(spotEx.MessageId);
			if (spotEx3 != null && !spotEx3.Body.IsNullOrEmpty() && spotEx2.Body.IsNullOrEmpty())
			{
				spotEx2.Body = spotEx3.Body;
			}
			if (spotEx3 != null && !spotEx3.ImageSource.IsNullOrEmpty() && spotEx2.ImageSource.IsNullOrEmpty())
			{
				spotEx2.ImageSource = spotEx3.ImageSource;
			}
			if (spotEx2.ImageSource.IsNullOrEmpty() && spotEx2.Body.IsNullOrEmpty())
			{
				return;
			}
		}
		FileCache.Save(spotEx2);
	}
}
