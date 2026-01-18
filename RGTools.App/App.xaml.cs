using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using RGTools.App.ViewModels;
using RGTools.App.Views;

namespace RGTools.App;

/// <summary>
/// Application entry point. Orchestrates the Tray Icon and Window lifecycle.
/// </summary>
public partial class App : Application
{
  private TaskbarIcon? _trayIcon;
  private TrayViewModel? _viewModel;

  // Tracks the active window instance to prevent duplicates.
  private DashboardView? _dashboardWindow;

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    // 1. Initialize ViewModel with the OpenWindow action injection
    _viewModel = new TrayViewModel(OpenDashboardWindow);

    // 2. Initialize TrayIcon
    _trayIcon = new TaskbarIcon
    {
      ToolTipText = "RGTools",
      DataContext = _viewModel,
      Visibility = Visibility.Visible,
      IconSource = new BitmapImage(new Uri("pack://application:,,,/RGTools.App;component/app.ico")),
      DoubleClickCommand = _viewModel.OpenDashboardCommand
    };

    // 3. Build Context Menu
    var contextMenu = new ContextMenu();

    contextMenu.Items.Add(new MenuItem
    {
      Header = "RGTools System",
      IsEnabled = false,
      FontWeight = FontWeights.Bold
    });

    contextMenu.Items.Add(new Separator());

    // Dashboard Item
    // Note: Using explicit binding or click event here relies on the Command execution from ViewModel.
    var dashItem = new MenuItem
    {
      Header = "Open Dashboard",
      FontWeight = FontWeights.SemiBold
    };
    // Explicitly routing the click to the Action for stability in headless mode
    dashItem.Click += (s, args) => _viewModel.OpenDashboardCommand.Execute(null);
    contextMenu.Items.Add(dashItem);

    contextMenu.Items.Add(new Separator());

    // Exit Item
    var exitItem = new MenuItem { Header = "Exit Core" };
    exitItem.Click += (s, args) => _viewModel.CloseCommand.Execute(null);
    contextMenu.Items.Add(exitItem);

    _trayIcon.ContextMenu = contextMenu;
    _trayIcon.ForceCreate();
  }

  /// <summary>
  /// Lifecycle Manager: Creates the window if null, or activates it if already open.
  /// Ensures "Zero Waste" by allowing the GC to collect the window when closed.
  /// </summary>
  private void OpenDashboardWindow()
  {
    if (_dashboardWindow == null)
    {
      _dashboardWindow = new DashboardView();
      _dashboardWindow.Closed += (s, e) => _dashboardWindow = null; // Reset reference on close
      _dashboardWindow.Show();
    }
    else
    {
      // If minimized or behind other windows, bring it to front
      if (_dashboardWindow.WindowState == WindowState.Minimized)
        _dashboardWindow.WindowState = WindowState.Normal;

      _dashboardWindow.Activate();
    }
  }

  protected override void OnExit(ExitEventArgs e)
  {
    _trayIcon?.Dispose();
    base.OnExit(e);
  }
}
