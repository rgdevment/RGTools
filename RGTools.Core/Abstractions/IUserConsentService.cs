namespace RGTools.App.Core;

public interface IUserConsentService
{
    bool IsGranted(string operationId);

    Task<bool> RequestAsync(string operationId, string title, string detail, bool remember = true);
}
