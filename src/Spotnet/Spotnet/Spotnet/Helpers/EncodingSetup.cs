using System.Runtime.CompilerServices;
using System.Text;

namespace Spotnet.Helpers;

/// <summary>
/// Makes the legacy Windows code pages available to this process.
/// </summary>
/// <remarks>
/// .NET Framework carried every code page in the box. .NET ships only UTF-8, UTF-16,
/// UTF-32, ASCII and Latin-1, and asking for anything else throws until a provider is
/// registered.
///
/// This is not optional here. `Microsoft.VisualBasic.Strings.Chr` resolves any value
/// above 127 through the system's ANSI code page, and the header parser calls it for
/// every spot it reads. Without this registration that call throws, the parser's
/// per-line handler swallows it, and the import quietly yields nothing at all - no error,
/// no log line, just no spots.
///
/// A module initializer rather than a startup call, so it holds for anything that loads
/// this assembly, tests included, and cannot be missed by a path that starts elsewhere.
/// </remarks>
internal static class EncodingSetup
{
	[ModuleInitializer]
	internal static void RegisterCodePages()
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
	}
}
