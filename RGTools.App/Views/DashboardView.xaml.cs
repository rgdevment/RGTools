using System.Collections.Generic;
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
    private readonly IProfileEngine _profiles;
    private readonly IUserConsentService _consent;
    private readonly IToolRegistry _tools;
    private readonly IToolProvisioner _provisioner;
    private readonly IToolLauncher _launcher;
    private readonly IToolArtifacts _artifacts;

    private const string GpuConsentId = "gaming.gpu-priority";

    private readonly Dictionary<string, Button> _toolButtons = new();
    private readonly Dictionary<string, Button> _artifactButtons = new();
    private readonly Dictionary<string, ProvisionState> _toolStates = new();

    public DashboardView(
        IConfigService config,
        IDnsGuardianService guardian,
        IVpnService vpnService,
        IJumpboxService jumpboxService,
        IStartupService startup,
        IProfileEngine profiles,
        IUserConsentService consent,
        IToolRegistry tools,
        IToolProvisioner provisioner,
        IToolLauncher launcher,
        IToolArtifacts artifacts)
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
            _profiles = profiles;
            _consent = consent;
            _tools = tools;
            _provisioner = provisioner;
            _launcher = launcher;
            _artifacts = artifacts;

            ChkDns.IsChecked = _config.Current.DnsGuardianEnabled;
            ChkStartup.IsChecked = _config.Current.StartWithWindows;

            _vpnService.StatusChanged += OnVpnStatusChanged;
            _vpnService.ConnectionChanged += OnVpnConnectionChanged;
            _profiles.ProfileChanged += OnProfileChanged;
            _profiles.DriftDetected += OnDriftDetected;

            ChkGpuPriority.IsChecked = _consent.IsGranted(GpuConsentId);
            UpdateProfileUi(_profiles.Active);
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

        await _tools.ReloadAsync();
        BuildToolTiles();
        await RefreshToolsAsync();
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

        Height = isActive ? 805 : 730;

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

    private async void BtnProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        if (!Enum.TryParse<ProfileKind>(tag, out var kind)) return;

        LogService.Log($"[UI-PROFILE] Apply requested: {kind}");
        SetProfileButtonsEnabled(false);
        try
        {
            await _profiles.ApplyAsync(kind);
        }
        catch (Exception ex)
        {
            LogService.Log("[UI-PROFILE] Apply failed", ex);
            MessageBox.Show($"No se pudo aplicar el perfil {ProfileCatalog.For(kind).DisplayName}.");
        }
        finally
        {
            SetProfileButtonsEnabled(true);
            UpdateProfileUi(_profiles.Active);
        }
    }

    private async void ChkGpuPriority_Click(object sender, RoutedEventArgs e)
    {
        bool granted = ChkGpuPriority.IsChecked ?? false;
        LogService.Log($"[UI-PROFILE] GPU Priority consent set to {granted}");

        await _config.UpdateAsync(s => s with
        {
            Consent = new ConsentSettings
            {
                Granted = new Dictionary<string, bool>(s.Consent.Granted) { [GpuConsentId] = granted }
            }
        });

        if (_profiles.Active == ProfileKind.Gaming)
            await _profiles.ApplyAsync(ProfileKind.Gaming);
    }

    private void OnProfileChanged(ProfileKind active)
    {
        Dispatcher.BeginInvoke(() => UpdateProfileUi(active));
    }

    private void OnDriftDetected(ProfileDrift drift)
    {
        Dispatcher.BeginInvoke(() => TxtProfileDrift.Visibility = Visibility.Visible);
    }

    private void UpdateProfileUi(ProfileKind active)
    {
        SetProfileButton(BtnBalanced, active == ProfileKind.Balanced);
        SetProfileButton(BtnWork, active == ProfileKind.Work);
        SetProfileButton(BtnGaming, active == ProfileKind.Gaming);

        TxtProfileDrift.Visibility = Visibility.Collapsed;
    }

    private void SetProfileButton(Button btn, bool isActive)
    {
        btn.Foreground = isActive ? (Brush)FindResource("Accent") : (Brush)FindResource("TextMain");
        btn.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
    }

    // The active profile stays clickable: reapplying it is how drift gets repaired.
    private void SetProfileButtonsEnabled(bool enabled)
    {
        BtnBalanced.IsEnabled = enabled;
        BtnWork.IsEnabled = enabled;
        BtnGaming.IsEnabled = enabled;
    }

    private void BuildToolTiles()
    {
        ToolsPanel.Children.Clear();
        _toolButtons.Clear();

        foreach (var tool in _tools.All)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var btn = new Button
            {
                Style = (Style)FindResource("ModeButton"),
                Tag = tool.Id,
                Content = tool.DisplayName,
                IsEnabled = false
            };
            btn.Click += BtnTool_Click;
            Grid.SetColumn(btn, 0);
            row.Children.Add(btn);
            _toolButtons[tool.Id] = btn;

            var artBtn = new Button
            {
                Style = (Style)FindResource("ModeButton"),
                Tag = tool.Id,
                Margin = new Thickness(8, 0, 0, 10),
                Visibility = Visibility.Collapsed
            };
            artBtn.Click += (_, _) =>
            {
                if (artBtn.ContextMenu is { Items.Count: > 0 } menu)
                {
                    menu.PlacementTarget = artBtn;
                    menu.IsOpen = true;
                }
            };
            Grid.SetColumn(artBtn, 1);
            row.Children.Add(artBtn);
            _artifactButtons[tool.Id] = artBtn;

            ToolsPanel.Children.Add(row);
        }
    }

    private async Task RefreshToolsAsync()
    {
        foreach (var tool in _tools.All)
        {
            if (!_toolButtons.TryGetValue(tool.Id, out var btn)) continue;

            ProvisionState state;
            try
            {
                state = await _provisioner.DetectAsync(tool);
            }
            catch (Exception ex)
            {
                LogService.Log($"[UI-TOOL] {tool.Id} detect failed", ex);
                state = ProvisionState.Broken;
            }

            _toolStates[tool.Id] = state;
            UpdateToolButton(tool, btn, state);
            RefreshArtifacts(tool);
        }
    }

    private void RefreshArtifacts(ToolDescriptor tool)
    {
        if (!_artifactButtons.TryGetValue(tool.Id, out var btn)) return;

        var group = _artifacts.List(tool).FirstOrDefault(g => g.Files.Count > 0);
        if (group == null)
        {
            btn.Visibility = Visibility.Collapsed;
            btn.ContextMenu = null;
            return;
        }

        var menu = new ContextMenu();
        foreach (var file in group.Files)
        {
            var item = new MenuItem { Header = $"{file.Name}   ({file.Modified:yyyy-MM-dd HH:mm})" };
            string path = file.FullPath;
            item.Click += (_, _) =>
            {
                if (!_artifacts.Open(path))
                    MessageBox.Show("No se pudo abrir el archivo.");
            };
            menu.Items.Add(item);
        }

        btn.Content = $"📄 {group.Files.Count}";
        btn.ToolTip = group.Label;
        btn.ContextMenu = menu;
        btn.Visibility = Visibility.Visible;
    }

    private static void UpdateToolButton(ToolDescriptor tool, Button btn, ProvisionState state)
    {
        (string text, bool enabled) = state switch
        {
            ProvisionState.Ready => ($"▶ Lanzar {tool.DisplayName}", true),
            ProvisionState.NotReady => ($"⚙ Preparar {tool.DisplayName}", true),
            ProvisionState.NotCloned => ($"⬇ Clonar {tool.DisplayName}", true),
            ProvisionState.Broken => ($"{tool.DisplayName} (sin manifiesto)", false),
            _ => ($"{tool.DisplayName} (no disponible)", false),
        };

        btn.Content = text;
        btn.IsEnabled = enabled;
    }

    private async void BtnTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;

        var tool = _tools.Find(id);
        if (tool == null) return;

        var state = _toolStates.GetValueOrDefault(id, ProvisionState.Broken);
        btn.IsEnabled = false;
        try
        {
            if (state == ProvisionState.Ready)
            {
                btn.Content = "Actualizando…";
                var update = await _provisioner.UpdateAsync(tool);
                if (update.Outcome == UpdateOutcome.Updated)
                {
                    // El pull pudo traer dependencias nuevas y los manifiestos lanzan con --no-sync.
                    btn.Content = "Preparando entorno…";
                    var ensure = await _provisioner.EnsureAsync(tool);
                    if (!ensure.Success)
                    {
                        ShowToolError($"La preparación de {tool.DisplayName} tras actualizar falló", ensure);
                        return;
                    }
                    await _tools.ReloadAsync();
                    tool = _tools.Find(id) ?? tool;
                }

                if (!_launcher.Launch(tool))
                    MessageBox.Show($"No se pudo lanzar {tool.DisplayName}.");
                return;
            }

            if (state == ProvisionState.NotCloned)
            {
                btn.Content = "Clonando…";
                var clone = await _provisioner.AcquireAsync(tool);
                if (!clone.Success)
                    ShowToolError($"La clonación de {tool.DisplayName} falló", clone);
                await _tools.ReloadAsync();
                await RefreshToolsAsync();
                return;
            }

            if (state == ProvisionState.NotReady)
            {
                btn.Content = "Preparando entorno…";
                var result = await _provisioner.EnsureAsync(tool);
                if (!result.Success)
                    ShowToolError($"La preparación de {tool.DisplayName} falló", result);
                await RefreshToolsAsync();
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"[UI-TOOL] {id} action failed", ex);
            MessageBox.Show($"Error con {tool.DisplayName}: {ex.Message}");
        }
        finally
        {
            var current = _tools.Find(id);
            if (current != null && _toolButtons.TryGetValue(id, out var b))
                UpdateToolButton(current, b, _toolStates.GetValueOrDefault(id, ProvisionState.Broken));
        }
    }

    private static string Tail(string text, int lines)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var nonEmpty = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0);
        return string.Join(Environment.NewLine, nonEmpty.TakeLast(lines));
    }

    private static void ShowToolError(string title, ToolRunResult result)
    {
        string detail = Tail(result.Output, 12);
        MessageBox.Show(
            $"{title} (código {result.ExitCode}).\n\n" +
            (string.IsNullOrWhiteSpace(detail) ? "El comando no produjo salida." : detail) +
            $"\n\nLog completo: {LogService.GetLogPath()}",
            "Herramientas", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        _profiles.ProfileChanged -= OnProfileChanged;
        _profiles.DriftDetected -= OnDriftDetected;
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
