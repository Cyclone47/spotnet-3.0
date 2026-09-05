using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace mshtml;

[ComImport]
[CompilerGenerated]
[Guid("3050F25A-98B5-11CF-BB82-00AA00BDCE0B")]
[TypeIdentifier]
public interface IHTMLSelectionObject
{
	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(1001)]
	[return: MarshalAs(UnmanagedType.IDispatch)]
	object createRange();
}
