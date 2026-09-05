using System;

namespace Spotnet.Controls;

internal interface ICloseableView : IDisposable
{
	void FocusDocument();
}
