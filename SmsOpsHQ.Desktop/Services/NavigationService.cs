using CommunityToolkit.Mvvm.ComponentModel;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Services;

// Manages navigation between views by switching the current ViewModel
// in the MainViewModel's ContentPresenter binding.
public sealed class NavigationService : ObservableObject
{
    private ViewModelBase? _currentView;
    private readonly Func<Type, ViewModelBase> _viewModelFactory;

    public NavigationService(Func<Type, ViewModelBase> viewModelFactory)
    {
        _viewModelFactory = viewModelFactory;
    }

    // The active ViewModel displayed in the main content area.
    public ViewModelBase? CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    // Navigate to a ViewModel of the specified type.
    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
    {
        ViewModelBase viewModel = _viewModelFactory(typeof(TViewModel));
        CurrentView = viewModel;
    }

    // Navigate to an already-created ViewModel instance.
    public void NavigateTo(ViewModelBase viewModel)
    {
        CurrentView = viewModel;
    }
}
