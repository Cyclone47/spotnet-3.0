using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace mshtml;

[ComImport]
[CompilerGenerated]
[InterfaceType(2)]
[DefaultMember("href")]
[Guid("3050F502-98B5-11CF-BB82-00AA00BDCE0B")]
[TypeIdentifier]
public interface DispHTMLAnchorElement
{
	void _VtblGap1_268();

	[DispId(0)]
	string href
	{
		[MethodImpl(MethodImplOptions.PreserveSig | MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(0)]
		[return: MarshalAs(UnmanagedType.BStr)]
		get;
		[MethodImpl(MethodImplOptions.PreserveSig | MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(0)]
		[param: MarshalAs(UnmanagedType.BStr)]
		set;
	}
}
