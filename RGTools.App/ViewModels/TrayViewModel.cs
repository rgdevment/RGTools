using System; // Required for Action
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RGTools.App.ViewModels;

/// <summary>
/// Interaction logic for the System Tray.
/// </summary>
public partial class TrayViewModel : ObservableObject
{
  private readonly Action _openWindowAction;

  /// <summary>
  /// Default constructor for XAML design-time support (if needed).
  /// </summary>
  public TrayViewModel()
  {
    _openWindowAction = () => { };
  }

  /// <summary>
  /// Injection constructor. Receives the window creation logic from App.xaml.cs.
  /// </summary>
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
    // Executes the delegate to open the View
    _openWindowAction?.Invoke();
  }
}
