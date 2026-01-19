using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RGTools.App.ViewModels;

public partial class TrayViewModel : ObservableObject
{
  private readonly Action _openWindowAction;

  public TrayViewModel(Action openWindowAction)
  {
    _openWindowAction = openWindowAction;
  }

  [RelayCommand]
  private void Close()
  {
    Application.Current.Shutdown();
  }

  [RelayCommand]
  private void OpenDashboard()
  {
    _openWindowAction?.Invoke();
  }
}
