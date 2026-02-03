using System.IO;
using System.Windows;
using RGTools.App.Core;

namespace RGTools.App.Views;

public partial class DashboardView : Window
{
    private readonly ConfigService _config;
    private readonly DnsGuardianService _guardian;
    private readonly VpnService _vpnService;
    private readonly CopilotService _copilotService;
    private readonly JumpboxService _jumpboxService;
    private FileSystemWatcher? _fileWatcher;

    public DashboardView(ConfigService config, DnsGuardianService guardian, VpnService vpnService)
    {
        LogService.Log("[UI] Initializing DashboardView components...");
        try
        {
            InitializeComponent();

            _config = config;
            _guardian = guardian;
            _vpnService = vpnService;
            _copilotService = new CopilotService(_config);

            ChkDns.IsChecked = _config.Current.DnsGuardianEnabled;

            _vpnService.StatusChanged += OnVpnStatusChanged;
            _vpnService.ConnectionChanged += OnVpnConnectionChanged;

            _jumpboxService = new JumpboxService(_config);
            _vpnService.StatusChanged += OnVpnStatusChanged;

            ChkStartup.IsChecked = _config.Current.StartWithWindows;

            Loaded += OnDashboardLoaded;

            UpdateVpnUi(_vpnService.IsActive);
            LogService.Log("[UI] DashboardView initialized successfully.");
        }
        catch (Exception ex)
        {
            LogService.Log("[UI FATAL] Failed to initialize Dashboard", ex);
            MessageBox.Show($"Error crítico de interfaz: {ex.Message}");
            throw;
        }
    }

    private void OnVpnConnectionChanged(bool isConnected)
    {
        LogService.Log($"[UI-VPN] Connection Event: {isConnected} | IP: {_vpnService.VpnIpAddress}");
        Dispatcher.Invoke(() =>
        {
            if (isConnected)
            {
                TxtVpnStatus.Text = $"● CONECTADO: {_vpnService.VpnIpAddress}";
                TxtVpnStatus.Visibility = Visibility.Visible;
            }
            else
            {
                TxtVpnStatus.Visibility = Visibility.Collapsed;
            }
        });
    }

    private async void ChkStartup_Click(object sender, RoutedEventArgs e)
    {
        bool isChecked = ChkStartup.IsChecked ?? false;
        LogService.Log($"[UI] Startup toggle: {isChecked}");

        try
        {
            await _config.SaveAsync(_config.Current with { StartWithWindows = isChecked });
            StartupService.SetStartup(isChecked);
        }
        catch (Exception ex)
        {
            LogService.Log("[STARTUP ERROR]", ex);
            MessageBox.Show($"Error al configurar inicio: {ex.Message}");
            ChkStartup.IsChecked = !isChecked;
        }
    }

    private void OnDashboardLoaded(object sender, RoutedEventArgs e)
    {
        LogService.Log("[UI] Dashboard loaded into view.");
        RefreshMeetingFiles();
    }

    private void OnVpnStatusChanged(bool isActive)
    {
        LogService.Log($"[VPN EVENT] Status received: {isActive}");
        Dispatcher.Invoke(() => UpdateVpnUi(isActive));
    }

    private void UpdateVpnUi(bool isActive)
    {
        BtnVpn.Content = isActive ? "Apagar servicio VPN" : "Encender servicio VPN";
        BtnVpn.Tag = isActive ? "ON" : "OFF";
        BtnVpn.IsEnabled = true;

        Height = isActive ? 660 : 600;

        BtnJumpbox.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;

        if (!isActive) TxtVpnStatus.Visibility = Visibility.Collapsed;
    }

    private async void BtnJumpbox_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("[UI] Launching Database Tunnel via WSL2...");

        BtnJumpbox.IsEnabled = false;
        var originalContent = BtnJumpbox.Content;
        BtnJumpbox.Content = "Validando...";

        try
        {
            await _jumpboxService.LaunchAsync();
        }
        catch (Exception ex)
        {
            LogService.Log("[UI-JUMPBOX] Failed to trigger launch", ex);
            MessageBox.Show("Error al intentar conectar con WSL2.");
        }
        finally
        {
            BtnJumpbox.Content = originalContent;
            BtnJumpbox.IsEnabled = true;
        }
    }

    private async void BtnVpn_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("[UI] VPN Toggle requested.");
        BtnVpn.IsEnabled = false;
        BtnVpn.Content = "Procesando...";

        try
        {
            await _vpnService.ToggleAsync();
        }
        catch (Exception ex)
        {
            LogService.Log("[VPN ERROR] Toggle failed", ex);
            MessageBox.Show("No se pudo cambiar el estado de la VPN.");
        }
        finally
        {
            UpdateVpnUi(_vpnService.IsActive);
        }
    }

    private async void ChkDns_Click(object sender, RoutedEventArgs e)
    {
        bool isChecked = ChkDns.IsChecked ?? false;
        LogService.Log($"[UI] DNS Guardian checkbox: {isChecked}");

        try
        {
            var newSettings = _config.Current with { DnsGuardianEnabled = isChecked };
            await _config.SaveAsync(newSettings);

            if (isChecked) _guardian.Start();
            else _guardian.Stop();
        }
        catch (Exception ex)
        {
            LogService.Log("[DNS ERROR]", ex);
            MessageBox.Show($"Error en configuración DNS: {ex.Message}");
            ChkDns.IsChecked = !isChecked;
        }
    }

    private async void BtnWorkOff_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("[UI] Work Off sequence initiated.");
        BtnWorkOff.IsEnabled = false;
        var originalContent = BtnWorkOff.Content;
        BtnWorkOff.Content = "Cerrando...";

        try
        {
            var workService = new WorkService(_vpnService);
            await workService.SwitchToWorkOffAsync();
        }
        catch (Exception ex)
        {
            LogService.Log("[WORKOFF ERROR]", ex);
        }
        finally
        {
            BtnWorkOff.Content = originalContent;
            BtnWorkOff.IsEnabled = true;
            LogService.Log("[UI] Work Off sequence completed.");
        }
    }

    private async void BtnCopilot_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("[UI] Meet Copilot launch requested.");
        BtnCopilot.IsEnabled = false;
        var prevContent = BtnCopilot.Content;
        BtnCopilot.Content = "Preparando...";

        try
        {
            await Task.Run(async () => await _copilotService.LaunchAsync());

            RefreshMeetingFiles();
        }
        catch (Exception ex)
        {
            LogService.Log("[COPILOT UI ERROR]", ex);
            MessageBox.Show("Error al iniciar Meet Copilot.");
        }
        finally
        {
            BtnCopilot.Content = prevContent;
            BtnCopilot.IsEnabled = true;
        }
    }

    private async void BtnResetPath_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("[UI] Manual path reset requested.");
        try
        {
            await _config.SaveAsync(_config.Current with { CopilotFolderPath = null });
            RefreshMeetingFiles();

            MessageBox.Show(
                "Ruta reiniciada.\nSe solicitará una nueva al iniciar Copilot.",
                "Configuración", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LogService.Log("[UI ERROR] Path reset failed", ex);
        }
    }

    private void RefreshMeetingFiles()
    {
        Dispatcher.Invoke(() =>
        {
            LogService.Log("[UI] Refreshing meeting logs list...");
            try
            {
                var files = _copilotService.GetMeetingFiles();
                CmbCopilotOptions.ItemsSource = files;

                if (files is { Count: > 0 })
                {
                    if (CmbCopilotOptions.SelectedIndex == -1)
                        CmbCopilotOptions.SelectedIndex = 0;
                }
                else
                {
                    CmbCopilotOptions.SelectedIndex = -1;
                }
            }
            catch (Exception ex)
            {
                LogService.Log("[UI ERROR] Failed to refresh meeting list", ex);
            }
        });
    }

    private void BtnOpenMeeting_Click(object sender, RoutedEventArgs e)
    {
        if (CmbCopilotOptions.SelectedItem is FileInfo selectedFile)
        {
            LogService.Log($"[UI] Opening file: {selectedFile.Name}");
            _copilotService.OpenMeetingFile(selectedFile.FullName);
        }
    }

    private void SetupFolderWatcher()
    {
        string? path = _config.Current.CopilotFolderPath;

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        _fileWatcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        _fileWatcher.Created += (s, e) => RefreshMeetingFiles();
        _fileWatcher.Deleted += (s, e) => RefreshMeetingFiles();
        _fileWatcher.Renamed += (s, e) => RefreshMeetingFiles();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("[UI] Closing Dashboard...");
        _vpnService.StatusChanged -= OnVpnStatusChanged;
        _vpnService.ConnectionChanged -= OnVpnConnectionChanged;

        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
        }

        this.Close();
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
