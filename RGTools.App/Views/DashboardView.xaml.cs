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
