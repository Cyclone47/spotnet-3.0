using System.Runtime.InteropServices;

namespace Spotnet;

internal static class NativeMethods
{
	[DllImport("kernel32.dll")]
	internal static extern ErrorModes SetErrorMode(ErrorModes mode);
}
