using Avalonia.Controls;
using Spotnet.Mac.ViewModels;

namespace Spotnet.Mac.Views;

public partial class SpotDetailWindow : Window
{
    public SpotDetailWindow()
    {
        InitializeComponent();
    }

    public SpotDetailWindow(SpotDetailViewModel vm) : this()
    {
        DataContext = vm;
    }
}
