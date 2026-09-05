namespace Spotnet.Mac.Models;

/// <summary>
/// Mirrors Windows Spotnet.ViewModel.PosterIdentType.
/// Categorizes the trust state of a spot's poster or message ID.
/// </summary>
public enum PosterIdentType
{
    Unspecified,
    None,
    Verified,
    White,
    Black,
    SpotWhite,
    SpotBlack,
    Fake
}
