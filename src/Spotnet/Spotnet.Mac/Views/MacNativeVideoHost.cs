using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Spotnet.Mac.Views;

/// <summary>
/// Sluit het NSView-oppervlak van de AVFoundation-speler in de Avalonia-visual
/// tree in — het Avalonia-tegenhanger van Windows' WPF-VideoView-hosting in
/// PlayerControl. De engine bezit de view; deze host levert hem alleen aan
/// het platform aan (HandleDescriptor "NSView").
/// </summary>
public sealed class MacNativeVideoHost : NativeControlHost
{
    private readonly IntPtr _nsView;

    public MacNativeVideoHost(IntPtr nsView)
    {
        _nsView = nsView;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        return new MacViewHandle(_nsView);
    }

    /// <summary>De engine is eigenaar; de host geeft de view nooit vrij.</summary>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
    }

    private sealed class MacViewHandle : IPlatformHandle
    {
        public MacViewHandle(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public string HandleDescriptor => "NSView";
    }
}
