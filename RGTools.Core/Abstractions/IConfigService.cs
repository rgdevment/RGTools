namespace RGTools.App.Core;

public interface IConfigService
{
    AppSettings Current { get; }

    Task LoadAsync();

    Task SaveAsync(AppSettings newSettings);

    Task UpdateAsync(Func<AppSettings, AppSettings> mutate);
}
