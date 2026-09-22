using System;
using System.IO;
using FluentAssertions;
using TopSpeed.Server.Config;
using TopSpeed.Server.Logging;
using Xunit;

namespace TopSpeed.Tests.Server
{
    /// <summary>
    /// The log level is written in one vocabulary and read in two places, the command line and the
    /// settings file, so what matters is that a level named in either means the same thing and that
    /// a settings file which has never heard of the option still ends up logging something.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class LogLevelSettingBehavior : IDisposable
    {
        private readonly string _folder;

        public LogLevelSettingBehavior()
        {
            _folder = Path.Combine(Path.GetTempPath(), "ts-levels-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public void NamedLevelsAreReadAndAnythingElseLeavesTheChoiceToTheCaller()
        {
            LogLevels.Parse("error,debug").Should().Be(LogLevel.Error | LogLevel.Debug);
            LogLevels.Parse("all").Should().Be(LogLevel.All);
            LogLevels.Parse(" INFO ").Should().Be(LogLevel.Info);
            LogLevels.Parse("chatty").Should().BeNull();
            LogLevels.Parse(string.Empty).Should().BeNull();
        }

        [Fact]
        public void ASettingsFileWithoutTheOptionIsGivenTheUsualThree()
        {
            var path = Path.Combine(_folder, "settings.json");
            File.WriteAllText(path, "{\"port\":28630}");

            using var logger = new Logger(LogLevel.None, logFilePath: null, writeToConsole: false);
            var settings = new ServerSettingsStore(path).LoadOrCreate(logger);

            LogLevels.Parse(settings.LogLevel).Should().Be(LogLevels.Default);
        }

        [Fact]
        public void AChosenLevelSurvivesBeingSavedAndReadBack()
        {
            var path = Path.Combine(_folder, "settings.json");
            using var logger = new Logger(LogLevel.None, logFilePath: null, writeToConsole: false);
            var store = new ServerSettingsStore(path);

            var settings = store.LoadOrCreate(logger);
            settings.LogLevel = LogLevels.Normalize("all");
            store.Save(settings, logger);

            LogLevels.Parse(store.LoadOrCreate(logger).LogLevel).Should().Be(LogLevel.All);
        }
    }
}
