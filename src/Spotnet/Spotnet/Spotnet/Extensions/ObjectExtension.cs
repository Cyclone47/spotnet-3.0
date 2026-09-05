using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Xml.Serialization;
using NLog;
using Newtonsoft.Json.Linq;
using Spotnet.Helpers;

namespace Spotnet.Extensions;

public static class ObjectExtension
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public static void Async(Action action)
	{
		action?.BeginInvoke(null, null);
	}

	public static void DispatchAsync(this DispatcherObject thisInstance, Action action)
	{
		if (action != null)
		{
			if (Thread.CurrentThread != thisInstance.Dispatcher.Thread)
			{
				thisInstance.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
			}
			else
			{
				action();
			}
		}
	}

	public static string ToStringSafely(this object target)
	{
		return target?.ToString() ?? "";
	}

	public static string SerializeObject<T>(this T toSerialize)
	{
		XmlSerializer xmlSerializer = new XmlSerializer(toSerialize.GetType());
		StringWriter stringWriter = new StringWriter();
		xmlSerializer.Serialize(stringWriter, toSerialize);
		return stringWriter.ToString();
	}

	public static void Forget(this Task task)
	{
		task?.ContinueWith(delegate(Task t)
		{
			if (t.Exception != null)
			{
				Log.Exception(t.Exception);
			}
		});
	}

	public static string ToResultString(this JObject obj)
	{
		return (obj?.GetValue("result"))?.Value<string>();
	}

	public static string ToErrorString(this JObject obj)
	{
		if (!(obj?.GetValue("error") is JObject jObject))
		{
			return null;
		}
		return jObject.GetValue("name").Value<string>() + ": " + jObject.GetValue("message").Value<string>();
	}

	public static void Sort<T>(this ObservableCollection<T> collection) where T : IComparable
	{
		List<T> list = collection.OrderBy((T x) => x).ToList();
		for (int i = 0; i < list.Count(); i++)
		{
			collection.Move(collection.IndexOf(list[i]), i);
		}
	}

	public static byte[] ToByteArray(this Image img)
	{
		using MemoryStream memoryStream = new MemoryStream();
		img.Save(memoryStream, ImageFormat.Png);
		return memoryStream.ToArray();
	}

	public static Bitmap Resize(this Image image, int width, int height)
	{
		Rectangle destRect = new Rectangle(0, 0, width, height);
		Bitmap bitmap = new Bitmap(width, height);
		bitmap.SetResolution(image.HorizontalResolution, image.VerticalResolution);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.CompositingMode = CompositingMode.SourceCopy;
		graphics.CompositingQuality = CompositingQuality.HighQuality;
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.SmoothingMode = SmoothingMode.HighQuality;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		using ImageAttributes imageAttributes = new ImageAttributes();
		imageAttributes.SetWrapMode(WrapMode.TileFlipXY);
		graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, imageAttributes);
		return bitmap;
	}
}
