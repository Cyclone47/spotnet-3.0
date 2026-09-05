using System;
using Spotnet.Controls;
using Spotnet.Model;

namespace Spotnet.Browser;

internal interface ISpotPage : IPage, ICloseableView, IDisposable
{
	SpotEx SpotEx { get; }
}
