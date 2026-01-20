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
  private readonly VpnService _vpnService = new();

  private TaskbarIcon? _trayIcon;
  private TrayViewModel? _viewModel;
  private DashboardView? _dashboardWindow;

  protected override async void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);
    LogService.Initialize();

    LogService.Log("[SYSTEM] Context confirmed: Administrator privileges granted via Manifest.");

    try
    {
      await _configService.LoadAsync();
      LogService.Log("[CONFIG] Loaded.");

      if (_configService.Current.DnsGuardianEnabled)
      {
        _dnsGuardian.Start();
      }

      InitializeTrayIcon();
    }
    catch (Exception ex)
    {
      LogService.Log("[CRITICAL] Bootstrap failed.", ex);
      Shutdown();
    }
  }

  private void InitializeTrayIcon()
  {
    _viewModel = new TrayViewModel(OpenDashboardWindow, _vpnService, _dnsGuardian);

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
    dashItem.Click += (_, _) => _viewModel.OpenDashboardCommand.Execute(null);

    var exitItem = new MenuItem { Header = "Exit RGTools" };
    exitItem.Click += (_, _) => _viewModel.CloseCommand.Execute(null);

    contextMenu.Items.Add(dashItem);
    contextMenu.Items.Add(new Separator());
    contextMenu.Items.Add(exitItem);

    _trayIcon.ContextMenu = contextMenu;
    _trayIcon.ForceCreate();

    LogService.Log("[UI] Tray Icon ready with VPN and DNS monitoring.");
  }

  private void OpenDashboardWindow()
  {
    if (_dashboardWindow == null)
    {
      _dashboardWindow = new DashboardView(_configService, _dnsGuardian, _vpnService);

      _dashboardWindow.Closed += (_, _) =>
      {
        _dashboardWindow = null;
        GC.Collect();
        LogService.Log("[UI] Dashboard destroyed.");
      };

      _dashboardWindow.Show();
    }

    _dashboardWindow.Activate();
    if (_dashboardWindow.WindowState == WindowState.Minimized)
      _dashboardWindow.WindowState = WindowState.Normal;
  }

  protected override void OnExit(ExitEventArgs e)
  {
    LogService.Log("[APP] Shutdown sequence initiated.");

    _trayIcon?.Dispose();
    _dnsGuardian.Stop();
    _vpnService.Dispose();

    base.OnExit(e);
  }
}
