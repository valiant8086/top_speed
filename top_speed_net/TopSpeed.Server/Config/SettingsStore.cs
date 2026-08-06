using System;
using System.IO;
using System.Text.Json;
using TopSpeed.Localization;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Updates;
using TopSpeed.Protocol;

namespace TopSpeed.Server.Config
{
    internal sealed class ServerSettingsStore
    {
        private readonly string _path;

        public ServerSettingsStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public ServerSettings LoadOrCreate(Logger logger)
        {
            if (!File.Exists(_path))
            {
                var settings = NormalizeSettings(new ServerSettings());
                Save(settings, logger);
                return settings;
            }

            try
            {
                var json = File.ReadAllText(_path);
                var settings = JsonSerializer.Deserialize(
                    json,
                    ServerSettingsJsonContext.Default.ServerSettings);
                return NormalizeSettings(settings ?? new ServerSettings());
            }
            catch (Exception ex)
            {
                logger.Warning(LocalizationService.Format(
                    LocalizationService.Mark("Failed to read server settings, using defaults: {0}"),
                    ex.Message));
                return NormalizeSettings(new ServerSettings());
            }
        }

        public void Save(ServerSettings settings, Logger logger)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                var json = JsonSerializer.Serialize(
                    settings,
                    ServerSettingsJsonContext.Default.ServerSettings);
                File.WriteAllText(_path, json);
            }
            catch (Exception ex)
            {
                logger.Warning(LocalizationService.Format(
                    LocalizationService.Mark("Failed to save server settings: {0}"),
                    ex.Message));
            }
        }

        private static ServerSettings NormalizeSettings(ServerSettings settings)
        {
            settings.Language = string.IsNullOrWhiteSpace(settings.Language)
                ? "en"
                : settings.Language.Trim();
            settings.Moderation ??= new ServerModerationSettings();
            settings.Features ??= new ServerFeaturesSettings();
            settings.Moderation.MaxNameLength = NormalizeMaxNameLength(settings.Moderation.MaxNameLength);
            settings.UpdateRuntimeAssetTag = ServerUpdateConfig.NormalizeConfiguredRuntimeAssetTag(settings.UpdateRuntimeAssetTag);
            settings.StartupUpdateMode = ResolveStartupUpdateMode(settings);
            settings.CheckForUpdatesOnStartup = null;
            settings.LogFile = (settings.LogFile ?? string.Empty).Trim();
            return settings;
        }

        private static string ResolveStartupUpdateMode(ServerSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.StartupUpdateMode))
                return StartupUpdateModes.Normalize(settings.StartupUpdateMode);

            // Settings file predates the three-way mode. The old switch only chose
            // between checking and not checking, so checking maps to notify.
            if (settings.CheckForUpdatesOnStartup == true)
                return StartupUpdateModes.Notify;

            return StartupUpdateModes.Off;
        }

        private static int NormalizeMaxNameLength(int value)
        {
            if (value < 1)
                return 40;

            if (value > ProtocolConstants.MaxPlayerNameLength)
                return ProtocolConstants.MaxPlayerNameLength;

            return value;
        }
    }
}
