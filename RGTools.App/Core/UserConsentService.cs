using System.Windows;

namespace RGTools.App.Core;

public sealed class UserConsentService : IUserConsentService
{
    private readonly IConfigService _config;

    public UserConsentService(IConfigService config) => _config = config;

    public bool IsGranted(string operationId)
        => _config.Current.Consent.Granted.TryGetValue(operationId, out var granted) && granted;

    public async Task<bool> RequestAsync(string operationId, string title, string detail, bool remember = true)
    {
        if (IsGranted(operationId)) return true;

        var app = Application.Current;
        if (app == null) return false;

        bool accepted = app.Dispatcher.Invoke(() =>
            MessageBox.Show(detail, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

        if (accepted && remember)
        {
            var updated = new Dictionary<string, bool>(_config.Current.Consent.Granted) { [operationId] = true };
            await _config.SaveAsync(_config.Current with { Consent = new ConsentSettings { Granted = updated } });
        }

        return accepted;
    }
}
