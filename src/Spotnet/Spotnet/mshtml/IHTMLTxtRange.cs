using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace mshtml;

[ComImport]
[CompilerGenerated]
[Guid("3050F220-98B5-11CF-BB82-00AA00BDCE0B")]
[TypeIdentifier]
public interface IHTMLTxtRange
{
	void _VtblGap1_1();

	[DispId(1004)]
	string text
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(1004)]
		[return: MarshalAs(UnmanagedType.BStr)]
		get;
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(1004)]
		[param: In]
		[param: MarshalAs(UnmanagedType.BStr)]
		set;
	}
}
