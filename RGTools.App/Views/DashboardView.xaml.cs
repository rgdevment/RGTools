using System.Windows;
using RGTools.App.Core;

namespace RGTools.App.Views;

public partial class DashboardView : Window
{
    private readonly IConfigService _config;
    private readonly IDnsGuardianService _guardian;
    private readonly IVpnService _vpnService;
    private readonly IJumpboxService _jumpboxService;

    public DashboardView(
        IConfigService config,
        IDnsGuardianService guardian,
        IVpnService vpnService,
        IJumpboxService jumpboxService)
    {
        LogService.Log("[UI] Initializing DashboardView components...");
        try
        {
            InitializeComponent();

            _config = config;
            _guardian = guardian;
            _vpnService = vpnService;
            _jumpboxService = jumpboxService;

            ChkDns.IsChecked = _config.Current.DnsGuardianEnabled;
            ChkStartup.IsChecked = _config.Current.StartWithWindows;

            _vpnService.StatusChanged += OnVpnStatusChanged;
            _vpnService.ConnectionChanged += OnVpnConnectionChanged;

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

        Height = isActive ? 410 : 320;

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

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("[UI] Closing Dashboard...");
        _vpnService.StatusChanged -= OnVpnStatusChanged;
        _vpnService.ConnectionChanged -= OnVpnConnectionChanged;

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
