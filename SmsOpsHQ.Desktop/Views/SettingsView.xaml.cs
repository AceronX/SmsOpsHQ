using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void DatabaseTab_Loaded(object sender, RoutedEventArgs e)
    {
        XpdPasswordBox.Password = "";
        if (DataContext is SettingsViewModel vm)
        {
            vm.XpdPassword = "";
            vm.ShowXpdPassword = false;
            vm.LoadDatabaseConfigCommand.Execute(null);
        }
        XpdPasswordEyeIcon.Text = "\uE7B3";
    }

    private void BrowseDatabasePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select SQLite database",
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true && DataContext is SettingsViewModel vm)
            vm.DatabasePath = dialog.FileName;
    }

    private void BrowseXpdPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select XPD file",
            Filter = "XPD / Access (*.XPD;*.mdb;*.accdb)|*.XPD;*.mdb;*.accdb|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true && DataContext is SettingsViewModel vm)
            vm.XpdFilePath = dialog.FileName;
    }

    private void BrowseMdwPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select MDW file",
            Filter = "MDW (*.mdw)|*.mdw|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true && DataContext is SettingsViewModel vm)
            vm.XpdMdwPath = dialog.FileName;
    }

    private void XpdPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && DataContext is SettingsViewModel vm)
            vm.XpdPassword = box.Password;
    }

    private void ToggleXpdPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        if (vm.ShowXpdPassword)
        {
            XpdPasswordBox.Password = vm.XpdPassword ?? string.Empty;
            vm.ShowXpdPassword = false;
            XpdPasswordEyeIcon.Text = "\uE7B3";
        }
        else
        {
            vm.XpdPassword = XpdPasswordBox.Password ?? string.Empty;
            vm.ShowXpdPassword = true;
            XpdPasswordEyeIcon.Text = "\uE740";
        }
    }

    private void CredentialsTab_Loaded(object sender, RoutedEventArgs e)
    {
        CredentialOldPassword.Password = "";
        CredentialNewPassword.Password = "";
        CredentialConfirmPassword.Password = "";
        if (DataContext is SettingsViewModel vm)
        {
            vm.ShowOldPassword = false;
            vm.ShowNewPassword = false;
            vm.ShowConfirmPassword = false;
            vm.LoadCredentialsCommand.Execute(null);
        }
        CredentialOldPasswordEyeIcon.Text = "\uE7B3";
        CredentialNewPasswordEyeIcon.Text = "\uE7B3";
        CredentialConfirmPasswordEyeIcon.Text = "\uE7B3";
    }

    private void CredentialPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (sender is not PasswordBox box) return;

        if (box == CredentialOldPassword)
            vm.OldPassword = box.Password;
        else if (box == CredentialNewPassword)
            vm.NewPassword = box.Password;
        else if (box == CredentialConfirmPassword)
            vm.ConfirmNewPassword = box.Password;
    }

    private void ToggleCredentialOldPassword_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (vm.ShowOldPassword)
        {
            CredentialOldPassword.Password = vm.OldPassword ?? string.Empty;
            vm.ShowOldPassword = false;
            CredentialOldPasswordEyeIcon.Text = "\uE7B3";
        }
        else
        {
            vm.OldPassword = CredentialOldPassword.Password ?? string.Empty;
            vm.ShowOldPassword = true;
            CredentialOldPasswordEyeIcon.Text = "\uE740";
        }
    }

    private void ToggleCredentialNewPassword_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (vm.ShowNewPassword)
        {
            CredentialNewPassword.Password = vm.NewPassword ?? string.Empty;
            vm.ShowNewPassword = false;
            CredentialNewPasswordEyeIcon.Text = "\uE7B3";
        }
        else
        {
            vm.NewPassword = CredentialNewPassword.Password ?? string.Empty;
            vm.ShowNewPassword = true;
            CredentialNewPasswordEyeIcon.Text = "\uE740";
        }
    }

    private void ToggleCredentialConfirmPassword_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (vm.ShowConfirmPassword)
        {
            CredentialConfirmPassword.Password = vm.ConfirmNewPassword ?? string.Empty;
            vm.ShowConfirmPassword = false;
            CredentialConfirmPasswordEyeIcon.Text = "\uE7B3";
        }
        else
        {
            vm.ConfirmNewPassword = CredentialConfirmPassword.Password ?? string.Empty;
            vm.ShowConfirmPassword = true;
            CredentialConfirmPasswordEyeIcon.Text = "\uE740";
        }
    }

    private async void PhoneNumbersTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            // Load stores first, then phone numbers after stores are loaded
            await vm.LoadStoresCommand.ExecuteAsync(null);
            await vm.LoadPhoneNumbersCommand.ExecuteAsync(null);
        }
    }

    private async void AddNewStoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var dialog = new AddStoreDialog
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        await vm.AddStoreByNameAsync(dialog.StoreName);
    }

    private void TwilioTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.ShowTwilioToken = false;
            TwilioTokenPasswordBox.Password = vm.TwilioToken ?? string.Empty;
        }
        TwilioTokenEyeIcon.Text = "\uE7B3";
    }

    private void TwilioTokenPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && DataContext is SettingsViewModel vm)
            vm.TwilioToken = box.Password;
    }

    private void ToggleTwilioTokenVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (vm.ShowTwilioToken)
        {
            TwilioTokenPasswordBox.Password = vm.TwilioToken ?? string.Empty;
            vm.ShowTwilioToken = false;
            TwilioTokenEyeIcon.Text = "\uE7B3";
        }
        else
        {
            vm.TwilioToken = TwilioTokenPasswordBox.Password ?? string.Empty;
            vm.ShowTwilioToken = true;
            TwilioTokenEyeIcon.Text = "\uE740";
        }
    }

    private void RemindersTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.LoadSchedulerStatusCommand.Execute(null);
    }
}
