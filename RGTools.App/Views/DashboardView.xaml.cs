using System.Windows;
using RGTools.App.Core;

namespace RGTools.App.Views;

public partial class DashboardView : Window
{
  private readonly ConfigService _config;
  private readonly DnsGuardianService _guardian;
  private readonly VpnService _vpnService;

  public DashboardView(ConfigService config, DnsGuardianService guardian, VpnService vpnService)
  {
    InitializeComponent();
    _config = config;
    _guardian = guardian;
    _vpnService = vpnService;

    ChkDns.IsChecked = _config.Current.DnsGuardianEnabled;

    _vpnService.StatusChanged += OnVpnStatusChanged;

    UpdateUi(_vpnService.IsActive);
  }

  private void OnVpnStatusChanged(bool isActive)
  {
    Dispatcher.Invoke(() => UpdateUi(isActive));
  }

  private void UpdateUi(bool isActive)
  {
    BtnVpn.Content = isActive ? "Apagar VPN" : "Encender VPN";
    BtnVpn.Tag = isActive ? "ON" : "OFF";

    BtnVpn.IsEnabled = true;
  }

  private async void BtnVpn_Click(object sender, RoutedEventArgs e)
  {
    BtnVpn.IsEnabled = false;
    BtnVpn.Content = "Procesando...";

    await _vpnService.ToggleAsync();

    UpdateUi(_vpnService.IsActive);
  }

  private async void ChkDns_Click(object sender, RoutedEventArgs e)
  {
    bool isChecked = ChkDns.IsChecked ?? false;

    try
    {
      var newSettings = _config.Current with { DnsGuardianEnabled = isChecked };
      await _config.SaveAsync(newSettings);

      if (isChecked) _guardian.Start();
      else _guardian.Stop();
    }
    catch (Exception ex)
    {
      MessageBox.Show($"Error: {ex.Message}", "Config", MessageBoxButton.OK, MessageBoxImage.Error);
      ChkDns.IsChecked = !isChecked;
    }
  }

  private async void BtnWorkOff_Click(object sender, RoutedEventArgs e)
  {
    BtnWorkOff.IsEnabled = false;
    var originalContent = BtnWorkOff.Content;
    BtnWorkOff.Content = "Cerrando...";

    var workService = new WorkService(_vpnService);
    await workService.SwitchToWorkOffAsync();

    BtnWorkOff.Content = originalContent;
    BtnWorkOff.IsEnabled = true;
  }

  private async void BtnCopilot_Click(object sender, RoutedEventArgs e)
  {
    // Bloqueo inmediato
    BtnCopilot.IsEnabled = false;
    var prevContent = BtnCopilot.Content;
    BtnCopilot.Content = "Preparando...";

    try
    {
      var service = new CopilotService(_config);

      await Task.Run(async () => await service.LaunchAsync());
    }
    catch (Exception ex)
    {
      LogService.Log("[UI ERROR]", ex);
      MessageBox.Show("Error al iniciar el servicio.");
    }
    finally
    {
      BtnCopilot.Content = prevContent;
      BtnCopilot.IsEnabled = true;
    }
  }

  private async void BtnResetPath_Click(object sender, RoutedEventArgs e)
  {
    try
    {
      await _config.SaveAsync(_config.Current with { CopilotFolderPath = null });

      MessageBox.Show(
          "La ruta de instalación ha sido reiniciada.\nSe te pedirá una nueva carpeta al iniciar Meet Copilot.",
          "Configuración",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
      LogService.Log("[UI] Error resetting path", ex);
    }
  }

  private void BtnClose_Click(object sender, RoutedEventArgs e)
  {
    _vpnService.StatusChanged -= OnVpnStatusChanged;
    Close();
  }

  protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
  {
    base.OnMouseLeftButtonDown(e);
    DragMove();
  }
}
