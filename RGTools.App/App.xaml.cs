using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using RGTools.App.Core;
using RGTools.App.ViewModels;
using RGTools.App.Views;

namespace RGTools.App;

public partial class App : Application
{
  private readonly ConfigService _configService = new();
  private readonly DnsGuardianService _dnsGuardian = new();

  private TaskbarIcon? _trayIcon;
  private TrayViewModel? _viewModel;

  private DashboardView? _dashboardWindow;

  protected override async void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    LogService.Initialize();

    if (AdminHelper.IsAdministrator())
    {
      LogService.Log("[App] Running with Administrator privileges.");
    }
    else
    {
      LogService.Log("[App] WARNING: Running as Standard User. Network features will be simulated.");
    }

    try
    {
      await _configService.LoadAsync();
      LogService.Log("[App] Configuration loaded.");

      if (_configService.Current.DnsGuardianEnabled)
      {
        _dnsGuardian.Start();
      }

      InitializeTrayIcon();
    }
    catch (Exception ex)
    {
      LogService.Log("CRITICAL FAILURE during startup", ex);
      Shutdown();
    }
  }

  private void InitializeTrayIcon()
  {
    _viewModel = new TrayViewModel(OpenDashboardWindow);

    _trayIcon = new TaskbarIcon
    {
      ToolTipText = "RGTools Suite",
      DataContext = _viewModel,
      Visibility = Visibility.Visible,
      IconSource = new BitmapImage(new Uri("pack://application:,,,/RGTools.App;component/app.ico")),
      DoubleClickCommand = _viewModel.OpenDashboardCommand
    };

    var contextMenu = new ContextMenu();

    var dashItem = new MenuItem { Header = "Dashboard", FontWeight = FontWeights.Bold };
    dashItem.Click += (s, args) => _viewModel.OpenDashboardCommand.Execute(null);

    var exitItem = new MenuItem { Header = "Exit RGTools" };
    exitItem.Click += (s, args) => _viewModel.CloseCommand.Execute(null);

    contextMenu.Items.Add(dashItem);
    contextMenu.Items.Add(new Separator());
    contextMenu.Items.Add(exitItem);

    _trayIcon.ContextMenu = contextMenu;
    _trayIcon.ForceCreate();

    LogService.Log("[App] Tray Icon initialized.");
  }

  private void OpenDashboardWindow()
  {
    if (_dashboardWindow == null)
    {
      _dashboardWindow = new DashboardView(_configService, _dnsGuardian);
      _dashboardWindow.Closed += (s, e) =>
      {
        _dashboardWindow = null;
        GC.Collect();
        LogService.Log("[UI] Dashboard closed and resources freed.");
      };
      _dashboardWindow.Show();
    }

    _dashboardWindow.Activate();

    if (_dashboardWindow.WindowState == WindowState.Minimized)
      _dashboardWindow.WindowState = WindowState.Normal;
  }

  protected override void OnExit(ExitEventArgs e)
  {
    LogService.Log("[App] Shutting down...");
    _trayIcon?.Dispose();
    _dnsGuardian.Stop();
    base.OnExit(e);
  }
}
