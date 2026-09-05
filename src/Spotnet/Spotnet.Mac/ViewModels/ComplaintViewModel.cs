using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;

namespace Spotnet.Mac.ViewModels;

/// <summary>
/// ViewModel for the complaint dialog (ComplainToTheSpot / Melding versturen).
/// Matches Windows ComplainToTheSpot.cs and Words.nl.resx.
/// </summary>
public sealed class ComplaintViewModel : ViewModelBase
{
    private readonly ComplaintService _complaintService;
    private string _reason = "";
    private bool _addToBlacklist = true;
    private bool _isSubmitting;
    private string _statusMessage = "";

    public SpotItem Spot { get; }
    public string SpotTitle => Spot.Subject;
    public string SpotSenderWithId => Spot.SenderWithId;

    public string Reason
    {
        get => _reason;
        set
        {
            if (SetProperty(ref _reason, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
                OnPropertyChanged(nameof(CharacterCountText));
            }
        }
    }

    public bool AddToBlacklist
    {
        get => _addToBlacklist;
        set => SetProperty(ref _addToBlacklist, value);
    }

    public bool IsSubmitting
    {
        get => _isSubmitting;
        set
        {
            if (SetProperty(ref _isSubmitting, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
                OnPropertyChanged(nameof(IsNotSubmitting));
            }
        }
    }

    public bool IsNotSubmitting => !IsSubmitting;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool CanSubmit
    {
        get
        {
            if (IsSubmitting) return false;
            int len = Reason?.Trim().Length ?? 0;
            return len >= 3 && len <= 900;
        }
    }

    public string CharacterCountText
    {
        get
        {
            int len = Reason?.Trim().Length ?? 0;
            return $"{len}/900";
        }
    }

    public ICommand SubmitCommand { get; }
    public ICommand CancelCommand { get; }

    public event Action? RequestClose;
    public event Action<bool, string>? ComplaintSubmitted;

    public ComplaintViewModel(SpotItem spot, ComplaintService complaintService)
    {
        Spot = spot ?? throw new ArgumentNullException(nameof(spot));
        _complaintService = complaintService ?? throw new ArgumentNullException(nameof(complaintService));

        SubmitCommand = new RelayCommand(async () => await SubmitAsync(), () => CanSubmit);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    public async Task SubmitAsync()
    {
        var (valid, error) = ComplaintService.ValidateDescription(Reason);
        if (!valid)
        {
            StatusMessage = error ?? "Ongeldige invoer.";
            return;
        }

        IsSubmitting = true;
        StatusMessage = "Melding plaatsen...";

        try
        {
            var (success, message) = await _complaintService.SubmitComplaintAsync(
                Spot,
                Reason,
                AddToBlacklist
            );

            if (success)
            {
                StatusMessage = message;
                ComplaintSubmitted?.Invoke(true, message);
                RequestClose?.Invoke();
            }
            else
            {
                IsSubmitting = false;
                StatusMessage = message;
                ComplaintSubmitted?.Invoke(false, message);
            }
        }
        catch (Exception ex)
        {
            IsSubmitting = false;
            StatusMessage = $"Fout: {ex.Message}";
        }
    }
}
