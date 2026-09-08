using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Spotnet.Mac.Media;

/// <summary>
/// Eén rij in de playlist van de Mac-speler — de port van Windows'
/// <c>PlaylistItemViewModel</c>: bestandspad, titel, duur en de
/// vetgedrukte markering van het nummer dat nu speelt.
/// </summary>
public sealed class MediaPlaylistItem : INotifyPropertyChanged
{
    private TimeSpan _length;
    private bool _isPlaying;

    public MediaPlaylistItem(string fileFullPath)
    {
        FileFullPath = fileFullPath;
    }

    public string FileFullPath { get; }

    public string Title => Path.GetFileName(FileFullPath);

    public string Directory => Path.GetDirectoryName(FileFullPath) ?? string.Empty;

    public TimeSpan Length
    {
        get => _length;
        set
        {
            if (_length != value)
            {
                _length = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DurationString));
            }
        }
    }

    public string DurationString => _length > TimeSpan.Zero ? FormatLength(_length) : string.Empty;

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                OnPropertyChanged();
            }
        }
    }

    public long FileSizeBytes
    {
        get
        {
            try
            {
                return File.Exists(FileFullPath) ? new FileInfo(FileFullPath).Length : 0;
            }
            catch (IOException)
            {
                return 0;
            }
        }
    }

    /// <summary>Korte duurnotatie mm:ss of h:mm:ss, zoals Windows' ToShortTimeString.</summary>
    public static string FormatLength(TimeSpan length)
    {
        if (length.TotalHours >= 1)
        {
            return $"{(int)length.TotalHours}:{length.Minutes:D2}:{length.Seconds:D2}";
        }

        return $"{length.Minutes}:{length.Seconds:D2}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
