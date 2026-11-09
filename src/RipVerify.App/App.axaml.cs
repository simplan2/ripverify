namespace RipVerify.App;

using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RipVerify.Core;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Services;
using RipVerify.Infrastructure;
using RipVerify.App.Services;
using RipVerify.App.ViewModels;
using RipVerify.App.Views;
using Serilog;

public partial class App : Application
{
    private bool _shutdownInProgress;

    public IServiceProvider? ServiceProvider { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            ConfigureServicesAndWindow();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed");
            throw;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServicesAndWindow()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));
        services.AddRipVerifyCore();
        services.AddRipVerifyInfrastructure();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<AlbumDetailViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<FirstRunWizardViewModel>();
        ServiceProvider = services.BuildServiceProvider();

        var settingsService = ServiceProvider.GetRequiredService<ISettingsService>();
        var localizationService = ServiceProvider.GetRequiredService<ILocalizationService>();

        Task.Run(() => settingsService.LoadAsync()).GetAwaiter().GetResult();
        localizationService.SetLanguage(settingsService.Current.DefaultLanguage);
        EnsureFfmpegPath(ServiceProvider, settingsService);

        var settings = settingsService.Current;
        Log.Information(
            "Settings loaded. CueToolsPath={CueToolsPath} (exists={CueToolsExists}) FfmpegPath={FfmpegPath} (exists={FfmpegExists})",
            settings.CueToolsPath,
            File.Exists(settings.CueToolsPath),
            settings.FfmpegPath,
            File.Exists(settings.FfmpegPath));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += OnShutdownRequested;

            if (settings.FirstRunCompleted)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = ServiceProvider.GetRequiredService<MainWindowViewModel>()
                };
            }
            else
            {
                desktop.MainWindow = new FirstRunWizardView
                {
                    DataContext = ServiceProvider.GetRequiredService<FirstRunWizardViewModel>()
                };
            }
        }
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownInProgress)
        {
            return;
        }

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            ServiceProvider is null)
        {
            return;
        }

        var viewModel = ServiceProvider.GetService<MainWindowViewModel>();
        if (viewModel is null || !viewModel.HasRunningWork)
        {
            return;
        }

        _shutdownInProgress = true;
        e.Cancel = true;

        try
        {
            await viewModel.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to cancel running work during shutdown");
        }
        finally
        {
            desktop.Shutdown(0);
        }
    }

    private static void EnsureFfmpegPath(IServiceProvider provider, ISettingsService settingsService)
    {
        var settings = settingsService.Current;
        if (!string.IsNullOrWhiteSpace(settings.FfmpegPath) && File.Exists(settings.FfmpegPath))
        {
            return;
        }

        try
        {
            var validator = provider.GetRequiredService<IFileValidator>();
            var resolved = validator.FindFfmpeg(settings.FfmpegPath);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                Log.Warning("No FFmpeg binary found; spectrum analysis will be unavailable");
                return;
            }

            settings.FfmpegPath = resolved;
            Task.Run(() => settingsService.SaveAsync(settings)).GetAwaiter().GetResult();
            Log.Information("Defaulted FFmpeg path to bundled/installed binary at {Path}", resolved);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to resolve a default FFmpeg path");
        }
    }
}

