using System;
using System.IO;
using TopSpeed.Server.Config;
using TopSpeed.Server.Logging;
using Xunit;

namespace TopSpeed.Tests;

[Trait("Category", "Behavior")]
public sealed class StartupUpdateModeMigrationBehaviorTests
{
    [Theory]
    [InlineData("true", StartupUpdateModes.Notify)]
    [InlineData("false", StartupUpdateModes.Off)]
    public void LoadOrCreate_ShouldCarryTheOldCheckForUpdatesSwitchOver(string legacyValue, string expectedMode)
    {
        using var settingsFile = new TemporarySettingsFile(
            $"{{ \"Language\": \"en\", \"CheckForUpdatesOnStartup\": {legacyValue} }}");

        var settings = settingsFile.Load();

        settings.StartupUpdateMode.Should().Be(expectedMode);
    }

    [Fact]
    public void LoadOrCreate_ShouldNotLetAStaleLegacySwitchOverrideAChosenMode()
    {
        using var settingsFile = new TemporarySettingsFile(
            "{ \"StartupUpdateMode\": \"auto\", \"CheckForUpdatesOnStartup\": false }");

        var settings = settingsFile.Load();

        settings.StartupUpdateMode.Should().Be(StartupUpdateModes.Auto);
    }

    [Fact]
    public void SavingAMigratedFileShouldDropTheLegacySwitch()
    {
        using var settingsFile = new TemporarySettingsFile(
            "{ \"CheckForUpdatesOnStartup\": true }");

        var settings = settingsFile.Load();
        settingsFile.Save(settings);

        // Once migrated the old key must not come back, or it would compete with the mode
        // on every later load.
        settingsFile.ReadRaw().Should().NotContain("CheckForUpdatesOnStartup");
        settingsFile.ReadRaw().Should().Contain(StartupUpdateModes.Notify);
    }

    [Theory]
    [InlineData("\"nonsense\"")]
    [InlineData("\"\"")]
    public void LoadOrCreate_ShouldFallBackToOffForAnUnrecognisedMode(string rawMode)
    {
        using var settingsFile = new TemporarySettingsFile($"{{ \"StartupUpdateMode\": {rawMode} }}");

        var settings = settingsFile.Load();

        settings.StartupUpdateMode.Should().Be(StartupUpdateModes.Off);
    }

    [Fact]
    public void AFreshSettingsFileShouldDefaultToOff()
    {
        using var settingsFile = new TemporarySettingsFile(contents: null);

        var settings = settingsFile.Load();

        settings.StartupUpdateMode.Should().Be(StartupUpdateModes.Off);
        settings.LogFile.Should().BeEmpty();
    }

    private sealed class TemporarySettingsFile : IDisposable
    {
        private readonly string _directory;
        private readonly string _path;
        private readonly Logger _logger = new(LogLevel.None, logFilePath: null, writeToConsole: false);

        public TemporarySettingsFile(string? contents)
        {
            _directory = Path.Combine(Path.GetTempPath(), "tsr-settings-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "settings.json");
            if (contents != null)
                File.WriteAllText(_path, contents);
        }

        public ServerSettings Load() => new ServerSettingsStore(_path).LoadOrCreate(_logger);

        public void Save(ServerSettings settings) => new ServerSettingsStore(_path).Save(settings, _logger);

        public string ReadRaw() => File.ReadAllText(_path);

        public void Dispose()
        {
            _logger.Dispose();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }
    }
}
