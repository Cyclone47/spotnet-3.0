using System;
using System.Windows;
using MahApps.Metro.Controls;
using Spotnet.Properties;

namespace Spotnet.Controls;

public partial class RemotePasswordWindow : MetroWindow
{
    internal string Password { get; private set; }

    public RemotePasswordWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordInput.Focus();
    }

    internal static string ValidatePasswords(string password, string confirmation)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return Words.RemotePasswordTooShort;
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            return Words.RemotePasswordMismatch;
        return null;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        string error = ValidatePasswords(PasswordInput.Password, ConfirmInput.Password);
        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Password = PasswordInput.Password;
        DialogResult = true;
    }
}
