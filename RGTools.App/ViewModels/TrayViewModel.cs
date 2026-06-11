using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RGTools.App.Core;

namespace RGTools.App.ViewModels;

public partial class TrayViewModel : ObservableObject, IDisposable
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

        _vpnService.StatusChanged += OnVpnStatusChanged;
        _dnsGuardian.StatusChanged += OnGuardianStatusChanged;
    }

    private void OnVpnStatusChanged(bool state) => IsVpnActive = state;

    private void OnGuardianStatusChanged(bool state) => IsGuardianActive = state;

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

    public void Dispose()
    {
        _vpnService.StatusChanged -= OnVpnStatusChanged;
        _dnsGuardian.StatusChanged -= OnGuardianStatusChanged;
    }
}
