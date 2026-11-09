using Avalonia;
using Serilog;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace RipVerify.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RipVerify",
            "logs");

        try
        {
            Directory.CreateDirectory(logDir);
        }
        catch
        {
        }

        try
        {
            Serilog.Debugging.SelfLog.Enable(message =>
            {
                try
                {
                    File.AppendAllText(
                        Path.Combine(logDir, "serilog-self.log"),
                        $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
                }
                catch
                {
                }
            });

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(logDir, "ripverify-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1),
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        catch (Exception ex)
        {
            WriteFallbackError(logDir, "Failed to configure Serilog", ex);
            throw;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            var exception = eventArgs.ExceptionObject as Exception;
            Log.Fatal(exception, "Unhandled exception (terminating: {IsTerminating})", eventArgs.IsTerminating);
            WriteFallbackError(logDir, "Unhandled exception", exception);
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Log.Error(eventArgs.Exception, "Unobserved task exception");
            eventArgs.SetObserved();
        };

        try
        {
            EncodingInitializer.Initialize();
            Log.Information(
                "RipVerify starting. Version={Version} BaseDirectory={BaseDirectory}",
                typeof(Program).Assembly.GetName().Version,
                AppContext.BaseDirectory);

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "RipVerify terminated unexpectedly");
            WriteFallbackError(logDir, "RipVerify terminated unexpectedly", ex);
            throw;
        }
        finally
        {
            Log.Information("RipVerify shutting down");
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void WriteFallbackError(string logDir, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}";
        try
        {
            File.AppendAllText(Path.Combine(logDir, "startup-error.log"), line);
        }
        catch
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "ripverify-startup-error.log"), line);
            }
            catch
            {
            }
        }
    }
}
