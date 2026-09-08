using System;

namespace Spotnet.Mac.Media;

/// <summary>
/// De afspeel-engine achter de Mac-speler — het Mac-tegenhanger van Windows'
/// <c>VlcPlayer</c>. De speler-VM praat uitsluitend met deze interface, zodat de
/// playlist-logica (port van <c>PlayerViewModel</c>) unit-getest kan worden met
/// een nep-engine en de echte engine er pas in de app achter hangt.
/// </summary>
public interface IMediaPlayerEngine : IDisposable
{
    /// <summary>Het native (NSView-)oppervlak waar de video in rendert.</summary>
    IntPtr VideoHandle { get; }

    /// <summary>Afspelen is bezig (rate &gt; 0).</summary>
    bool IsPlaying { get; }

    /// <summary>Afstempeling binnen de media.</summary>
    TimeSpan Time { get; set; }

    /// <summary>Totale duur; 0 zolang de media nog niet geanalyseerd is.</summary>
    TimeSpan Length { get; }

    /// <summary>Positie als fractie 0..1.</summary>
    double Position { get; set; }

    /// <summary>Volume 0..200, zoals het Windows-volumeschuifje.</summary>
    int Volume { get; set; }

    bool IsMute { get; set; }

    /// <summary>Laadt een bestand (video of audio) en koppelt het aan de engine.</summary>
    void Load(string filePath);

    /// <summary>Past het native videovlak aan de actuele Avalonia-grootte aan.</summary>
    void Resize(double width, double height);

    void Play();

    void Pause();
}
