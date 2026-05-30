using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RGTools.App.Core;

namespace RGTools.App.Views;

public partial class DashboardView : Window
{
    private readonly IConfigService _config;
    private readonly IDnsGuardianService _guardian;
    private readonly IVpnService _vpnService;
    private readonly IJumpboxService _jumpboxService;
    private readonly IStartupService _startup;
    private readonly IModeManager _modeManager;
    private readonly IKillAllService _killAll;

    public DashboardView(
        IConfigService config,
        IDnsGuardianService guardian,
        IVpnService vpnService,
        IJumpboxService jumpboxService,
        IStartupService startup,
        IModeManager modeManager,
        IKillAllService killAll)
    {
        LogService.Log("[UI] Initializing DashboardView components...");
        try
        {
            InitializeComponent();

            _config = config;
            _guardian = guardian;
            _vpnService = vpnService;
            _jumpboxService = jumpboxService;
            _startup = startup;
            _modeManager = modeManager;
            _killAll = killAll;

            ChkDns.IsChecked = _config.Current.DnsGuardianEnabled;
            ChkStartup.IsChecked = _config.Current.StartWithWindows;

            _vpnService.StatusChanged += OnVpnStatusChanged;
            _vpnService.ConnectionChanged += OnVpnConnectionChanged;
            _modeManager.ModeChanged += OnModeChanged;

            UpdateModeUi(_modeManager.Active);
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

            if (!await _startup.SetStartupAsync(isChecked))
                throw new InvalidOperationException("schtasks no pudo aplicar el cambio.");
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

        Height = isActive ? 635 : 560;

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

    private async void BtnMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        if (!Enum.TryParse<ProfileKind>(tag, out var kind)) return;

        LogService.Log($"[UI-MODE] Switch requested: {kind}");
        SetModeButtonsEnabled(false);
        try
        {
            await _modeManager.SwitchToAsync(kind);
        }
        catch (Exception ex)
        {
            LogService.Log("[UI-MODE] Switch failed", ex);
            MessageBox.Show($"No se pudo cambiar al perfil {kind}.");
        }
        finally
        {
            SetModeButtonsEnabled(true);
            UpdateModeUi(_modeManager.Active);
        }
    }

    private async void BtnKillAll_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("¿Apagar todo y cerrar RGTools?", "Apagar Todo",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        LogService.Log("[UI] Kill All requested.");
        try
        {
            await _killAll.ExecuteAsync();
        }
        catch (Exception ex)
        {
            LogService.Log("[UI] Kill All failed", ex);
        }
    }

    private void OnModeChanged(ProfileKind active)
    {
        Dispatcher.Invoke(() => UpdateModeUi(active));
    }

    private void UpdateModeUi(ProfileKind active)
    {
        SetModeButton(BtnWork, active == ProfileKind.Work);
        SetModeButton(BtnGaming, active == ProfileKind.Gaming);
        SetModeButton(BtnZen, active == ProfileKind.Zen);
    }

    private void SetModeButton(Button btn, bool isActive)
    {
        btn.Foreground = isActive ? (Brush)FindResource("Accent") : (Brush)FindResource("TextMain");
        btn.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
    }

    private void SetModeButtonsEnabled(bool enabled)
    {
        BtnWork.IsEnabled = enabled;
        BtnGaming.IsEnabled = enabled;
        BtnZen.IsEnabled = enabled;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("[UI] Closing Dashboard...");
        this.Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _vpnService.StatusChanged -= OnVpnStatusChanged;
        _vpnService.ConnectionChanged -= OnVpnConnectionChanged;
        _modeManager.ModeChanged -= OnModeChanged;
        base.OnClosed(e);
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
