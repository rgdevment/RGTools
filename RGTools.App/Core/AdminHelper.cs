using System.Security.Principal;

namespace RGTools.App.Core;

public static class AdminHelper
{
    private static bool? _isAdministrator;

    public static bool IsAdministrator()
    {
        // Cacheamos el resultado (Micro-optimización)
        if (_isAdministrator.HasValue) return _isAdministrator.Value;

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        _isAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);

        return _isAdministrator.Value;
    }
}
