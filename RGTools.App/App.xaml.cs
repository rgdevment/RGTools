using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RGTools.App.Core;
using RGTools.App.ViewModels;
using RGTools.App.Views;

namespace RGTools.App;

public partial class App : Application
{
    private IHost? _host;
    private TaskbarIcon? _trayIcon;
    private TrayViewModel? _viewModel;
    private DashboardView? _dashboardWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        LogService.Initialize();
        LogService.Log("[SYSTEM] Application starting...");
        LogService.Log("[SYSTEM] Context confirmed: Administrator privileges granted via Manifest.");

        try
        {
            _host = BuildHost();
            await _host.StartAsync();

            var config = _host.Services.GetRequiredService<IConfigService>();
            await config.LoadAsync();
            LogService.Log("[CONFIG] Loaded.");

            if (config.Current.DnsGuardianEnabled)
            {
                LogService.Log("[CONFIG] DNS Guardian is enabled, starting service...");
                _host.Services.GetRequiredService<IDnsGuardianService>().Start();
            }
            else
            {
                LogService.Log("[CONFIG] DNS Guardian is disabled in config.");
            }

            var modeManager = _host.Services.GetRequiredService<IModeManager>();
            if (modeManager.Active != ProfileKind.Work)
            {
                LogService.Log($"[MODE] Recovering from previous '{modeManager.Active}' session -> Work");
                await modeManager.SwitchToAsync(ProfileKind.Work);
            }

            InitializeTrayIcon();
        }
        catch (Exception ex)
        {
            LogService.LogCrash("[CRITICAL] Bootstrap failed", ex);
            Shutdown();
        }
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;

        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<ISystemStateStore, SystemStateStore>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddSingleton<IUserConsentService, UserConsentService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();

        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IDnsGuardianService, DnsGuardianService>();
        services.AddSingleton<IVpnService, VpnService>();
        services.AddSingleton<IJumpboxService, JumpboxService>();

        services.AddSingleton<IPowerPlanService, PowerPlanService>();
        services.AddSingleton<IWorkloadGuard, WorkloadGuardService>();
        services.AddSingleton<IMode, WorkModeService>();
        services.AddSingleton<IMode, GamingModeService>();
        services.AddSingleton<IMode, ZenModeService>();
        services.AddSingleton<IModeManager, ModeManager>();
        services.AddSingleton<IKillAllService, KillAllService>();

        services.AddSingleton<HealthCheckService>();
        services.AddHostedService(sp => sp.GetRequiredService<HealthCheckService>());

        services.AddSingleton<TrayViewModel>();
        services.AddTransient<DashboardView>();

        return builder.Build();
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
        e.Handled = true;
        LogService.Log("[RECOVERY] Exception handled, continuing execution.");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogService.LogCrash("[ERROR] Unobserved Task Exception", e.Exception);
        e.SetObserved();
        LogService.Log("[RECOVERY] Task exception observed, continuing execution.");
    }

    private void InitializeTrayIcon()
    {
        _viewModel = _host!.Services.GetRequiredService<TrayViewModel>();
        _viewModel.OpenDashboardRequested += OpenDashboardWindow;

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

        _host.Services.GetRequiredService<NotificationService>().Attach(_trayIcon);

        var health = _host.Services.GetRequiredService<HealthCheckService>();
        health.StatusChanged += OnHealthStatusChanged;

        LogService.Log("[UI] Tray Icon ready with VPN and DNS monitoring.");
    }

    private void OnHealthStatusChanged(string tooltip)
    {
        Dispatcher.Invoke(() =>
        {
            if (_trayIcon != null) _trayIcon.ToolTipText = tooltip;
        });
    }

    private void OpenDashboardWindow()
    {
        if (_dashboardWindow == null)
        {
            _dashboardWindow = _host!.Services.GetRequiredService<DashboardView>();

            _dashboardWindow.Closed += (_, _) =>
            {
                _dashboardWindow = null;
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

        if (_host != null)
        {
            try
            {
                _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogService.Log("[APP] Host stop error", ex);
            }

            _host.Dispose();
            LogService.Log("[APP] Host disposed (singletons released).");
        }

        LogService.Log("[APP] Shutdown completed.");
        base.OnExit(e);
    }
}
