using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace mshtml;

[ComImport]
[CompilerGenerated]
[Guid("332C4425-26CB-11D0-B483-00C04FD90119")]
[TypeIdentifier]
public interface IHTMLDocument2 : IHTMLDocument
{
	void _VtblGap1_14();

	[DispId(1017)]
	IHTMLSelectionObject selection
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(1017)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}
}
