using Avalonia.Controls;
using Spotnet.Mac.ViewModels;

namespace Spotnet.Mac.Views;

public partial class ComplaintWindow : Window
{
    public ComplaintWindow()
    {
        InitializeComponent();
    }

    public ComplaintWindow(ComplaintViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose += Close;

        Opened += (_, _) =>
        {
            var textBox = this.FindControl<TextBox>("ReasonTextBox");
            textBox?.Focus();
        };
    }
}
