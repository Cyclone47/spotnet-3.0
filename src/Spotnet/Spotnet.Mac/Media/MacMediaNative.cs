using System;
using System.Runtime.InteropServices;

namespace Spotnet.Mac.Media;

/// <summary>
/// Dunne Objective-C-runtime-laag voor de AVFoundation-speler. De Mac-client
/// bundelt geen LibVLC (de native VLC-engine is geen deel van deze Mac-build);
/// in plaats daarvan gebruikt het de in macOS aanwezige AVPlayer via
/// objc_msgSend — hetzelfde pad dat elke macOS-videospeler onder water gebruikt.
///
/// Let op de ABI-details die hier vastliggen: macOS-BOOL is 1 byte (U1),
/// CMTime is een 24-bytes struct die op x86_64 via objc_msgSend_stret
/// terugkomt, en float-returns zitten in XMM0 (daarom de _f32-variant).
/// </summary>
internal static class MacMediaNative
{
    private const string ObjCLib = "/usr/lib/libobjc.dylib";
    private const string AvFoundationPath = "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";

    [DllImport(ObjCLib)] internal static extern IntPtr objc_getClass(string name);
    [DllImport(ObjCLib)] internal static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCLib)] internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel);
    [DllImport(ObjCLib)] internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel, IntPtr arg1);
    [DllImport(ObjCLib)] internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel, IntPtr arg1, IntPtr arg2);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel, IntPtr arg1, UIntPtr arg2);
    [DllImport(ObjCLib)] internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel, CGRect rect);
    [DllImport(ObjCLib)] internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel, UIntPtr arg1);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    internal static extern IntPtr objc_msgSend_with_byte(IntPtr receiver, IntPtr sel, byte arg1);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    internal static extern IntPtr objc_msgSend_with_double(IntPtr receiver, IntPtr sel, double arg1);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    internal static extern IntPtr objc_msgSend_with_cmtime(IntPtr receiver, IntPtr sel, CMTime time);

    /// <summary>Float-teruggevende aanroepen (bijv. [AVPlayer rate]): lees XMM0 als float.</summary>
    [DllImport(ObjCLib)] internal static extern float objc_msgSend_f32(IntPtr receiver, IntPtr sel);

    /// <summary>CMTime-teruggevende aanroepen (24 bytes) gaan via msgSend_stret op x86_64.</summary>
    [DllImport(ObjCLib)]
    internal static extern void objc_msgSend_stret(out CMTime ret, IntPtr receiver, IntPtr sel);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr dlopen(string path, int mode);

    private static readonly IntPtr NSStringClass = objc_getClass("NSString");
    private static readonly IntPtr StringWithCharacters = sel_registerName("stringWithCharacters:length:");
    private static readonly IntPtr LengthSel = sel_registerName("length");
    private static readonly IntPtr Utf8Sel = sel_registerName("UTF8String");

    /// <summary>Is AVFoundation bruikbaar op dit systeem?</summary>
    internal static bool TryLoadFrameworks()
    {
        try
        {
            // RTLD_NOW = 2. AVFoundation trekt Foundation/QuartzCore mee.
            if (dlopen(AvFoundationPath, 2) == IntPtr.Zero)
            {
                return false;
            }

            return objc_getClass("AVPlayer") != IntPtr.Zero
                   && objc_getClass("AVPlayerLayer") != IntPtr.Zero
                   && objc_getClass("NSView") != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    internal static IntPtr Class(string name) => objc_getClass(name);

    internal static IntPtr Selector(string name) => sel_registerName(name);

    /// <summary>Bouwt een autoreleased NSString uit een C#-string.</summary>
    internal static IntPtr ToNSString(string value)
    {
        var buffer = Marshal.StringToCoTaskMemUni(value);
        try
        {
            return objc_msgSend(NSStringClass, StringWithCharacters, buffer, (UIntPtr)value.Length);
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    /// <summary>Leest een NSString terug als C#-string (voor debugging; null-veilig).</summary>
    internal static string FromNSString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
        {
            return string.Empty;
        }

        var utf8 = objc_msgSend(nsString, Utf8Sel);
        if (utf8 == IntPtr.Zero)
        {
            return string.Empty;
        }

        return Marshal.PtrToStringUTF8(utf8) ?? string.Empty;
    }

    /// <summary>
    /// De lengte van een NSString in tekens — de speler gebruikt dit niet,
    /// maar het houdt de helper-bib volledig bruikbaar voor toekomstige aanroepen.
    /// </summary>
    internal static int NsStringLength(IntPtr nsString) =>
        nsString == IntPtr.Zero ? 0 : (int)(long)objc_msgSend(nsString, LengthSel);
}

/// <summary>CoreGraphics-rect voor NSView/CALayer-aanroepen (32 bytes, MEMORY-klasse).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CGRect
{
    public double X, Y, Width, Height;

    public static CGRect Zero => default;
}

/// <summary>CoreMedia-tijdstempel (24 bytes: value, timescale, flags, epoch).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CMTime
{
    public long Value;
    public int Timescale;
    public int Flags;
    public long Epoch;

    public static CMTime Make(long value, int timescale) => new() { Value = value, Timescale = timescale };

    /// <summary>600 Hz is de door AVFoundation aangeraden timescale voor video.</summary>
    public static CMTime FromSeconds(double seconds) =>
        Make((long)Math.Round(Math.Max(0, seconds) * 600.0), 600);

    public readonly double Seconds => Timescale == 0 ? 0 : (double)Value / Timescale;
}
