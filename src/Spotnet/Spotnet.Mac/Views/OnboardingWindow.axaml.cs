using Avalonia.Controls;
using Spotnet.Mac.ViewModels;

namespace Spotnet.Mac.Views;

public partial class OnboardingWindow : Window
{
    public OnboardingWindow()
    {
        InitializeComponent();
    }

    public OnboardingWindow(OnboardingViewModel vm) : this()
    {
        DataContext = vm;
    }
}
