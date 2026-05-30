using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RGTools.App.Core;

namespace RGTools.App.ViewModels;

public partial class TrayViewModel : ObservableObject
{
    private readonly IVpnService _vpnService;
    private readonly IDnsGuardianService _dnsGuardian;

    [ObservableProperty]
    private bool _isVpnActive;

    [ObservableProperty]
    private bool _isGuardianActive;

    public event Action? OpenDashboardRequested;

    public TrayViewModel(IVpnService vpnService, IDnsGuardianService dnsGuardian)
    {
        _vpnService = vpnService;
        _dnsGuardian = dnsGuardian;

        _isVpnActive = _vpnService.IsActive;
        _isGuardianActive = _dnsGuardian.IsRunning;

        _vpnService.StatusChanged += state => IsVpnActive = state;
        _dnsGuardian.StatusChanged += state => IsGuardianActive = state;
    }

    [RelayCommand]
    private void Close()
    {
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private void OpenDashboard()
    {
        OpenDashboardRequested?.Invoke();
    }
}
