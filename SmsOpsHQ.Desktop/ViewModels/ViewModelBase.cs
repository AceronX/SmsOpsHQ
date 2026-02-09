using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmsOpsHQ.Desktop.ViewModels;

// Base class for all view models. Provides property change notification
// and command infrastructure via CommunityToolkit.Mvvm.
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    // Clear any displayed error.
    protected void ClearError() => ErrorMessage = string.Empty;

    // Set an error message for display.
    protected void SetError(string message) => ErrorMessage = message;
}
