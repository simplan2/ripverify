namespace RipVerify.Core.Services;

using RipVerify.Core.Models;

public interface ISettingsService
{
    string SettingsFilePath { get; }
    AppSettings Current { get; }
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    AppSettings GetDefaults();
}
