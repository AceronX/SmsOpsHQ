using System.Windows.Controls;
using System.Windows.Input;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Sync PasswordBox to ViewModel on each keystroke.
            PasswordBox.PasswordChanged += (_, _) =>
            {
                if (DataContext is LoginViewModel vm)
                    vm.Password = PasswordBox.Password;
            };
        };
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is LoginViewModel vm)
            vm.LoginCommand.Execute(null);
    }
}
