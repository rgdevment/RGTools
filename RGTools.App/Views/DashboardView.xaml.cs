using System.Linq;
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
    private readonly IToolRegistry _tools;
    private readonly IToolProvisioner _provisioner;
    private readonly IToolLauncher _launcher;

    private ToolDescriptor? _videomerge;
    private ProvisionState _videomergeState = ProvisionState.NotCloned;

    public DashboardView(
        IConfigService config,
        IDnsGuardianService guardian,
        IVpnService vpnService,
        IJumpboxService jumpboxService,
        IStartupService startup,
        IModeManager modeManager,
        IToolRegistry tools,
        IToolProvisioner provisioner,
        IToolLauncher launcher)
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
            _tools = tools;
            _provisioner = provisioner;
            _launcher = launcher;

            ChkDns.IsChecked = _config.Current.DnsGuardianEnabled;
            ChkStartup.IsChecked = _config.Current.StartWithWindows;

            _vpnService.StatusChanged += OnVpnStatusChanged;
            _vpnService.ConnectionChanged += OnVpnConnectionChanged;
            _modeManager.ModeChanged += OnModeChanged;

            UpdateModeUi(_modeManager.Active);
            UpdateVpnUi(_vpnService.IsActive);
            Loaded += OnDashboardLoaded;
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
        ChkStartup.IsEnabled = false;

        try
        {
            await _config.UpdateAsync(s => s with { StartWithWindows = isChecked });

            if (!await _startup.SetStartupAsync(isChecked))
                throw new InvalidOperationException("schtasks no pudo aplicar el cambio.");
        }
        catch (Exception ex)
        {
            LogService.Log("[STARTUP ERROR]", ex);
            MessageBox.Show($"Error al configurar inicio: {ex.Message}");
            ChkStartup.IsChecked = !isChecked;
        }
        finally
        {
            ChkStartup.IsEnabled = true;
        }
    }

    private async void OnDashboardLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            bool? realState = await _startup.IsEnabledAsync();
            if (realState is bool state && state != (ChkStartup.IsChecked ?? false))
            {
                ChkStartup.IsChecked = state;
                await _config.UpdateAsync(s => s with { StartWithWindows = state });
                LogService.Log($"[UI] Startup checkbox reconciled to real task state: {state}");
            }
        }
        catch (Exception ex)
        {
            LogService.Log("[UI] Startup reconcile failed", ex);
        }

        await RefreshVideomergeAsync();
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

        Height = isActive ? 700 : 625;

        BtnJumpbox.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;

        if (!isActive) TxtVpnStatus.Visibility = Visibility.Collapsed;
    }

    private async void BtnJumpbox_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("[UI] Launching Database Tunnel via WSL2...");

        string? path = _config.Current.JumboxFolderPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = PromptForJumpboxPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            await _config.UpdateAsync(s => s with { JumboxFolderPath = path });
        }

        BtnJumpbox.IsEnabled = false;
        var originalContent = BtnJumpbox.Content;
        BtnJumpbox.Content = "Validando...";

        try
        {
            var result = await _jumpboxService.LaunchAsync(path);
            if (!result.Success)
                MessageBox.Show(result.Error ?? "Error al conectar con WSL2.", "Jumpbox", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private string? PromptForJumpboxPath()
    {
        const string example = "/home/mario/code/github_work/jumbox";
        string inputPath = string.Empty;

        var dialog = new Window
        {
            Title = "Configuración Jumpbox",
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow,
            Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
            Foreground = Brushes.White,
            ResizeMode = ResizeMode.NoResize,
            Owner = this
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "Ruta WSL2 (ej: /home/.../jumbox):", Margin = new Thickness(0, 0, 0, 10) });

        var txtInput = new TextBox
        {
            Text = example,
            Padding = new Thickness(5),
            Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1)
        };
        stack.Children.Add(txtInput);

        var btnOk = new Button { Content = "Aceptar", Margin = new Thickness(0, 15, 0, 0), Height = 30, IsDefault = true };
        btnOk.Click += (_, _) => { inputPath = txtInput.Text; dialog.DialogResult = true; };
        stack.Children.Add(btnOk);

        dialog.Content = stack;
        return dialog.ShowDialog() == true ? inputPath : null;
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
        ChkDns.IsEnabled = false;

        try
        {
            await _config.UpdateAsync(s => s with { DnsGuardianEnabled = isChecked });

            if (isChecked) _guardian.Start();
            else _guardian.Stop();
        }
        catch (Exception ex)
        {
            LogService.Log("[DNS ERROR]", ex);
            MessageBox.Show($"Error en configuración DNS: {ex.Message}");
            ChkDns.IsChecked = !isChecked;
        }
        finally
        {
            ChkDns.IsEnabled = true;
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

    private void OnModeChanged(ProfileKind active)
    {
        Dispatcher.BeginInvoke(() => UpdateModeUi(active));
    }

    private void UpdateModeUi(ProfileKind active)
    {
        SetModeButton(BtnWork, active == ProfileKind.Work);
        SetModeButton(BtnGaming, active == ProfileKind.Gaming);
        SetModeButton(BtnBoost, active == ProfileKind.Boost);
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
        BtnBoost.IsEnabled = enabled;
    }

    private async Task RefreshVideomergeAsync()
    {
        _videomerge = _tools.Find("videomerge");
        if (_videomerge == null)
        {
            _videomergeState = ProvisionState.NotCloned;
            UpdateToolUi();
            return;
        }

        try
        {
            _videomergeState = await _provisioner.DetectAsync(_videomerge);
        }
        catch (Exception ex)
        {
            LogService.Log("[UI-TOOL] videomerge detect failed", ex);
            _videomergeState = ProvisionState.Broken;
        }

        UpdateToolUi();
    }

    private void UpdateToolUi()
    {
        (string text, bool enabled) = _videomergeState switch
        {
            ProvisionState.Ready => ("▶ Lanzar videomerge", true),
            ProvisionState.NotReady => ("⚙ Preparar videomerge", true),
            ProvisionState.Broken => ("videomerge (sin manifiesto)", false),
            _ => ("videomerge (no encontrado)", false),
        };

        BtnVideomerge.Content = text;
        BtnVideomerge.IsEnabled = enabled;
    }

    private static string Tail(string text, int lines)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var nonEmpty = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0);
        return string.Join(Environment.NewLine, nonEmpty.TakeLast(lines));
    }

    private async void BtnTool_Click(object sender, RoutedEventArgs e)
    {
        if (_videomerge == null) return;

        BtnVideomerge.IsEnabled = false;
        try
        {
            if (_videomergeState == ProvisionState.Ready)
            {
                if (!_launcher.Launch(_videomerge))
                    MessageBox.Show("No se pudo lanzar videomerge.");
                return;
            }

            if (_videomergeState == ProvisionState.NotReady)
            {
                BtnVideomerge.Content = "Preparando entorno…";
                var result = await _provisioner.EnsureAsync(_videomerge);
                if (!result.Success)
                {
                    string detail = Tail(result.Output, 12);
                    MessageBox.Show(
                        $"La preparación de videomerge falló (código {result.ExitCode}).\n\n" +
                        (string.IsNullOrWhiteSpace(detail) ? "El comando no produjo salida." : detail) +
                        $"\n\nLog completo: {LogService.GetLogPath()}",
                        "videomerge", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                await RefreshVideomergeAsync();
            }
        }
        catch (Exception ex)
        {
            LogService.Log("[UI-TOOL] videomerge action failed", ex);
            MessageBox.Show($"Error con videomerge: {ex.Message}");
        }
        finally
        {
            UpdateToolUi();
        }
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
