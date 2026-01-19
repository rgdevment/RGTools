using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Security.Principal;
using H.NotifyIcon;
using RGTools.App.Core;
using RGTools.App.ViewModels;
using RGTools.App.Views;
using static RGTools.App.Core.LogService;

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
    LogService.Log($"App started (Admin: {IsAdministrator()})");

    await _configService.LoadAsync();

    if (_configService.Current.DnsGuardianEnabled)
    {
      _dnsGuardian.Start();
    }

    _viewModel = new TrayViewModel(OpenDashboardWindow);

    _trayIcon = new TaskbarIcon
    {
      ToolTipText = "RGTools",
      DataContext = _viewModel,
      Visibility = Visibility.Visible,
      IconSource = new BitmapImage(new Uri("pack://application:,,,/RGTools.App;component/app.ico")),
      DoubleClickCommand = _viewModel.OpenDashboardCommand
    };

    // Menú Contextual Simple
    var contextMenu = new ContextMenu();
    var dashItem = new MenuItem { Header = "Open Dashboard", FontWeight = FontWeights.Bold };
    dashItem.Click += (s, args) => _viewModel.OpenDashboardCommand.Execute(null);
    contextMenu.Items.Add(dashItem);

    var exitItem = new MenuItem { Header = "Exit Core" };
    exitItem.Click += (s, args) => _viewModel.CloseCommand.Execute(null);
    contextMenu.Items.Add(exitItem);

    _trayIcon.ContextMenu = contextMenu;
    _trayIcon.ForceCreate();
  }

  private void OpenDashboardWindow()
  {
    if (_dashboardWindow == null)
    {
      _dashboardWindow = new DashboardView(_configService, _dnsGuardian);

      _dashboardWindow.Closed += (s, e) => _dashboardWindow = null;

      _dashboardWindow.Show();
      _dashboardWindow.Activate();
    }
    else
    {
      if (_dashboardWindow.WindowState == WindowState.Minimized)
        _dashboardWindow.WindowState = WindowState.Normal;

      _dashboardWindow.Activate();
    }
  }

  protected override void OnExit(ExitEventArgs e)
  {
    _trayIcon?.Dispose();
    _dnsGuardian.Stop();
    LogService.Shutdown();
    base.OnExit(e);
  }

  private static bool IsAdministrator()
  {
    using var identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
  }
}
