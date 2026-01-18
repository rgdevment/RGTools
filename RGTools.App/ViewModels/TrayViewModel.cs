using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RGTools.App.ViewModels;

public partial class TrayViewModel : ObservableObject
{
  [RelayCommand]
  private void Close()
  {
    Application.Current.Shutdown();
  }

  [RelayCommand]
  private void OpenConfig()
  {
    try
    {
      MessageBox.Show(
          "El módulo de configuración se está cargando...",
          "RGTools Config",
          MessageBoxButton.OK,
          MessageBoxImage.Information,
          MessageBoxResult.OK,
          MessageBoxOptions.DefaultDesktopOnly);
    }
    catch (System.Exception ex)
    {
      MessageBox.Show($"Error: {ex.Message}");
    }
  }
}
