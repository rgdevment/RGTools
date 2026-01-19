using System.Windows;
using RGTools.App.Core;

namespace RGTools.App.Views;

public partial class DashboardView : Window
{
  private readonly ConfigService _config;
  private readonly DnsGuardianService _guardian;

  public DashboardView(ConfigService config, DnsGuardianService guardian)
  {
    InitializeComponent();
    _config = config;
    _guardian = guardian;

    ChkDns.IsChecked = _config.Current.DnsGuardianEnabled;
  }

  private async void ChkDns_Click(object sender, RoutedEventArgs e)
  {
    bool isChecked = ChkDns.IsChecked ?? false;

    try
    {
      var newSettings = _config.Current with { DnsGuardianEnabled = isChecked };

      await _config.SaveAsync(newSettings);

      if (isChecked)
        _guardian.Start();
      else
        _guardian.Stop();
    }
    catch (Exception ex)
    {
      MessageBox.Show($"Error saving configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      ChkDns.IsChecked = !isChecked;
    }
  }

  private void BtnClose_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }

  protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
  {
    base.OnMouseLeftButtonDown(e);
    DragMove();
  }
}
