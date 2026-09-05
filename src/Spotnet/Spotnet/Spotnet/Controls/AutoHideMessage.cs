using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media.Animation;

namespace Spotnet.Controls;
public partial class AutoHideMessage : UserControl
{
    public AutoHideMessage(string message)
    {
        InitializeComponent();
        TextBlock.Text = message;
        ((Storyboard)FindResource("FadeIn")).Begin();
    }

    private void FadeIn_OnCompleted(object sender, EventArgs e)
    {
        if (base.Parent is Popup { IsOpen: false })
        {
            LayoutRoot.Visibility = Visibility.Collapsed;
        }
    }
}