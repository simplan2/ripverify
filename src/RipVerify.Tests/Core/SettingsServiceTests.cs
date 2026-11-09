namespace RipVerify.Tests.Core;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using System.IO;
using System.Threading.Tasks;

public class SettingsServiceTests
{
    [Fact]
    public async Task LoadAsyncCreatesDefaultsWhenMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "RipVerifyTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");
        
        var logger = NullLogger<SettingsService>.Instance;
        var service = new TestableSettingsService(logger, settingsPath);
        
        var settings = await service.LoadAsync();
        
        settings.Should().NotBeNull();
        settings.LogLevel.Should().Be("Information");
        settings.MusicBrainzUserAgent.Should().Be(AppSettings.DefaultMusicBrainzUserAgent);
        File.Exists(settingsPath).Should().BeTrue();

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task LoadAsyncReplacesPlaceholderUserAgent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "RipVerifyTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """{"musicBrainzUserAgent":"RipVerify/1.0 (https://github.com/<org>/ripverify; <contacto>)"}""");

        var service = new TestableSettingsService(NullLogger<SettingsService>.Instance, settingsPath);

        var settings = await service.LoadAsync();

        settings.MusicBrainzUserAgent.Should().Be(AppSettings.DefaultMusicBrainzUserAgent);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task SaveAsyncNeverPersistsPlaceholderUserAgent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "RipVerifyTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");
        var service = new TestableSettingsService(NullLogger<SettingsService>.Instance, settingsPath);

        await service.SaveAsync(new AppSettings
        {
            MusicBrainzUserAgent = "RipVerify/1.0 (<contacto>)"
        });

        var saved = await File.ReadAllTextAsync(settingsPath);
        saved.Should().NotContain("<");
        saved.Should().NotContain(">");
        saved.Should().Contain(AppSettings.DefaultMusicBrainzUserAgent);

        Directory.Delete(tempDir, true);
    }

    private sealed class TestableSettingsService : SettingsService
    {
        private readonly string _path;
        public TestableSettingsService(Microsoft.Extensions.Logging.ILogger<SettingsService> logger, string path) : base(logger)
        {
            _path = path;
        }
        public override string SettingsFilePath => _path;
    }
}
