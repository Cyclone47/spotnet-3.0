using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace mshtml;

[ComImport]
[CompilerGenerated]
[Guid("3050F240-98B5-11CF-BB82-00AA00BDCE0B")]
[TypeIdentifier]
public interface IHTMLImgElement
{
	void _VtblGap1_42();

	[DispId(-2147418107)]
	int width
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(-2147418107)]
		get;
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(-2147418107)]
		[param: In]
		set;
	}

	[DispId(-2147418106)]
	int height
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(-2147418106)]
		get;
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(-2147418106)]
		[param: In]
		set;
	}
}
