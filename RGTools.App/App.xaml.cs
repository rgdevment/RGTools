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

        // Capture unhandled exceptions globally
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        LogService.Initialize();
        LogService.Log("[SYSTEM] Application starting...");
        LogService.Log("[SYSTEM] Context confirmed: Administrator privileges granted via Manifest.");

        try
        {
            await _configService.LoadAsync();
            LogService.Log("[CONFIG] Loaded.");

            if (_configService.Current.DnsGuardianEnabled)
            {
                LogService.Log("[CONFIG] DNS Guardian is enabled, starting service...");
                _dnsGuardian.Start();
            }
            else
            {
                LogService.Log("[CONFIG] DNS Guardian is disabled in config.");
            }

            InitializeTrayIcon();
        }
        catch (Exception ex)
        {
            LogService.LogCrash("[CRITICAL] Bootstrap failed", ex);
            Shutdown();
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        LogService.LogCrash("[FATAL] Unhandled AppDomain Exception", ex ?? new Exception("Unknown exception"));

        if (e.IsTerminating)
        {
            LogService.Log("[FATAL] Application is terminating due to unhandled exception.");
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.LogCrash("[ERROR] Unhandled Dispatcher Exception", e.Exception);
        e.Handled = true; // Prevent app from crashing
        LogService.Log("[RECOVERY] Exception handled, continuing execution.");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogService.LogCrash("[ERROR] Unobserved Task Exception", e.Exception);
        e.SetObserved(); // Prevent app from crashing
        LogService.Log("[RECOVERY] Task exception observed, continuing execution.");
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
        LogService.Log($"[APP] Exit code: {e.ApplicationExitCode}");

        try
        {
            _trayIcon?.Dispose();
            LogService.Log("[APP] Tray icon disposed.");
        }
        catch (Exception ex)
        {
            LogService.Log("[APP] Tray icon disposal error", ex);
        }

        try
        {
            _dnsGuardian.Stop();
            LogService.Log("[APP] DNS Guardian stopped.");
        }
        catch (Exception ex)
        {
            LogService.Log("[APP] DNS Guardian stop error", ex);
        }

        try
        {
            _vpnService.Dispose();
            LogService.Log("[APP] VPN Service disposed.");
        }
        catch (Exception ex)
        {
            LogService.Log("[APP] VPN Service disposal error", ex);
        }

        LogService.Log("[APP] Shutdown completed.");
        base.OnExit(e);
    }
}
