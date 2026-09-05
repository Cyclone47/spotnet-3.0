using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace Spotnet.Properties;

[GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", "15.0.0.0")]
[DebuggerNonUserCode]
[CompilerGenerated]
public class Resources
{
	private static ResourceManager resourceMan;

	private static CultureInfo resourceCulture;

	[EditorBrowsable(EditorBrowsableState.Advanced)]
	public static ResourceManager ResourceManager
	{
		get
		{
			if (resourceMan == null)
			{
				resourceMan = new ResourceManager("Spotnet.Properties.Resources", typeof(Resources).Assembly);
			}
			return resourceMan;
		}
	}

	[EditorBrowsable(EditorBrowsableState.Advanced)]
	public static CultureInfo Culture
	{
		get
		{
			return resourceCulture;
		}
		set
		{
			resourceCulture = value;
		}
	}

	public static Icon about => (Icon)ResourceManager.GetObject("about", resourceCulture);

	public static Icon add => (Icon)ResourceManager.GetObject("add", resourceCulture);

	public static Icon addspot => (Icon)ResourceManager.GetObject("addspot", resourceCulture);

	public static string badwords => ResourceManager.GetString("badwords", resourceCulture);

	public static Bitmap bansky2 => (Bitmap)ResourceManager.GetObject("bansky2", resourceCulture);

	public static string BuildDate => ResourceManager.GetString("BuildDate", resourceCulture);

	public static Icon button => (Icon)ResourceManager.GetObject("button", resourceCulture);

	public static Icon close => (Icon)ResourceManager.GetObject("close", resourceCulture);

	public static Icon columns => (Icon)ResourceManager.GetObject("columns", resourceCulture);

	public static byte[] Default_theme => (byte[])ResourceManager.GetObject("Default_theme", resourceCulture);

	public static Icon delete => (Icon)ResourceManager.GetObject("delete", resourceCulture);

	public static Icon down => (Icon)ResourceManager.GetObject("down", resourceCulture);

	public static Icon downloads => (Icon)ResourceManager.GetObject("downloads", resourceCulture);

	public static string dummy => ResourceManager.GetString("dummy", resourceCulture);

	public static Icon favorite => (Icon)ResourceManager.GetObject("favorite", resourceCulture);

	public static Icon filter => (Icon)ResourceManager.GetObject("filter", resourceCulture);

	public static string FiltersAdvanced => ResourceManager.GetString("FiltersAdvanced", resourceCulture);

	public static string FiltersAdvanced_en => ResourceManager.GetString("FiltersAdvanced_en", resourceCulture);

	public static Bitmap focus => (Bitmap)ResourceManager.GetObject("focus", resourceCulture);

	public static Icon font2 => (Icon)ResourceManager.GetObject("font2", resourceCulture);

	public static Icon fontsize => (Icon)ResourceManager.GetObject("fontsize", resourceCulture);

	public static Icon info2 => (Icon)ResourceManager.GetObject("info2", resourceCulture);

	public static Icon nofilter => (Icon)ResourceManager.GetObject("nofilter", resourceCulture);

	public static byte[] nzbget_conf => (byte[])ResourceManager.GetObject("nzbget_conf", resourceCulture);

	public static Icon open => (Icon)ResourceManager.GetObject("open", resourceCulture);

	public static Bitmap refresh => (Bitmap)ResourceManager.GetObject("refresh", resourceCulture);

	public static Bitmap refresh2 => (Bitmap)ResourceManager.GetObject("refresh2", resourceCulture);

	public static string ReleaseNotes => ResourceManager.GetString("ReleaseNotes", resourceCulture);

	public static string ReleaseNotes_en => ResourceManager.GetString("ReleaseNotes_en", resourceCulture);

	public static string ReleaseNotesCss => ResourceManager.GetString("ReleaseNotesCss", resourceCulture);

	public static Icon resume => (Icon)ResourceManager.GetObject("resume", resourceCulture);

	public static Icon rows => (Icon)ResourceManager.GetObject("rows", resourceCulture);

	public static Icon save => (Icon)ResourceManager.GetObject("save", resourceCulture);

	public static Icon search => (Icon)ResourceManager.GetObject("search", resourceCulture);

	public static Icon settings => (Icon)ResourceManager.GetObject("settings", resourceCulture);

	public static Icon smallspotnet => (Icon)ResourceManager.GetObject("smallspotnet", resourceCulture);

	public static string SnowFall => ResourceManager.GetString("SnowFall", resourceCulture);

	public static Bitmap splash => (Bitmap)ResourceManager.GetObject("splash", resourceCulture);

	public static Bitmap spot1 => (Bitmap)ResourceManager.GetObject("spot1", resourceCulture);

	public static Icon trash => (Icon)ResourceManager.GetObject("trash", resourceCulture);

	public static Icon up => (Icon)ResourceManager.GetObject("up", resourceCulture);

	public static Icon url => (Icon)ResourceManager.GetObject("url", resourceCulture);

	public static string vlcrc => ResourceManager.GetString("vlcrc", resourceCulture);

	public static Icon warning => (Icon)ResourceManager.GetObject("warning", resourceCulture);

	public static string whatsnew => ResourceManager.GetString("whatsnew", resourceCulture);

	public static string whatsnew_nl => ResourceManager.GetString("whatsnew_nl", resourceCulture);

	internal Resources()
	{
	}
}
