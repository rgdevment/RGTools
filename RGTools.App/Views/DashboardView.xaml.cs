using System.Windows;
using System.Windows.Input;

namespace RGTools.App.Views;

/// <summary>
/// Main dashboard window interaction logic.
/// </summary>
public partial class DashboardView : Window
{
  public DashboardView()
  {
    InitializeComponent();

    // Window Drag Logic
    this.MouseDown += (s, e) =>
    {
      if (e.ChangedButton == MouseButton.Left)
        this.DragMove();
    };
  }

  /// <summary>
  /// Closes the window. The application instance remains alive in the Tray
  /// thanks to ShutdownMode="OnExplicitShutdown" in App.xaml.
  /// </summary>
  private void BtnClose_Click(object sender, RoutedEventArgs e)
  {
    this.Close();
  }
}
