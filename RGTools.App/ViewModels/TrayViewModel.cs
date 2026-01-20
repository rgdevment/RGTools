using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RGTools.App.Core;

namespace RGTools.App.ViewModels;

public partial class TrayViewModel : ObservableObject
{
  private readonly Action _openWindowAction;
  private readonly VpnService _vpnService;

  [ObservableProperty]
  private bool _isVpnActive;

  [ObservableProperty]
  private bool _isGuardianActive;

  private readonly DnsGuardianService _dnsGuardian;

  public TrayViewModel(Action openWindowAction, VpnService vpnService, DnsGuardianService dnsGuardian)
  {
    _openWindowAction = openWindowAction;
    _vpnService = vpnService;
    _dnsGuardian = dnsGuardian;

    _isVpnActive = _vpnService.IsActive;
    _isGuardianActive = _dnsGuardian.IsRunning;


    _vpnService.StatusChanged += (state) => IsVpnActive = state;
    _dnsGuardian.StatusChanged += (state) => IsGuardianActive = state;
  }

  [RelayCommand]
  private void Close()
  {
    Application.Current.Shutdown();
  }

  [RelayCommand]
  private void OpenDashboard()
  {
    _openWindowAction?.Invoke();
  }
}
