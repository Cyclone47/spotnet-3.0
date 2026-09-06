using Spotnet.Mac.Models;

namespace Spotnet.Mac.ViewModels;

/// <summary>
/// Base for the things the main area can show. Windows puts the spot list and every
/// opened spot in one tab strip; this is what that strip is bound to.
/// </summary>
public abstract class WorkspaceTabViewModel : ViewModelBase
{
    public abstract string Header { get; }

    /// <summary>The overview tab has no close button, as on Windows.</summary>
    public virtual bool CanClose => true;

    /// <summary>
    /// Icoon vóór de titel voor de vaste tabbladen (☁ Overzicht, ⬇ Downloads),
    /// zoals Windows' tabstrook; leeg voor spot-tabbladen.
    /// </summary>
    public virtual string TabIcon => "";
}

/// <summary>The spot list — always the first tab and never closable.</summary>
public sealed class OverviewTabViewModel : WorkspaceTabViewModel
{
    public override string Header => "Overzicht";
    public override bool CanClose => false;
    public override string TabIcon => "☁";
}

/// <summary>One opened spot, shown in its own tab.</summary>
public sealed class SpotTabViewModel : WorkspaceTabViewModel
{
    public SpotItem Spot { get; }
    public SpotDetailViewModel Detail { get; }

    public override string Header => Spot.Subject;

    public SpotTabViewModel(SpotItem spot, SpotDetailViewModel detail)
    {
        Spot = spot;
        Detail = detail;
        Detail.Spot = spot;
    }
}
