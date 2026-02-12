using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmsOpsHQ.Desktop.ViewModels;

// Base class for all view models. Provides property change notification
// and command infrastructure via CommunityToolkit.Mvvm.
public abstract partial class ViewModelBase : ObservableObject
{
    private const int ErrorDismissSeconds = 5;
    private DispatcherTimer? _errorDismissTimer;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    protected void ClearError() => ErrorMessage = string.Empty;

    protected void SetError(string message)
    {
        _errorDismissTimer?.Stop();
        _errorDismissTimer = null;
        ErrorMessage = message;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        _errorDismissTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(ErrorDismissSeconds)
        };
        _errorDismissTimer.Tick += OnErrorDismissTick;
        _errorDismissTimer.Start();
    }

    private void OnErrorDismissTick(object? sender, EventArgs e)
    {
        if (_errorDismissTimer is null) return;
        _errorDismissTimer.Tick -= OnErrorDismissTick;
        _errorDismissTimer.Stop();
        _errorDismissTimer = null;
        ErrorMessage = string.Empty;
    }
}
