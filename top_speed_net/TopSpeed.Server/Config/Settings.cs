using System.Text.Json.Serialization;

namespace TopSpeed.Server.Config
{
    internal sealed class ServerSettings
    {
        public string Language { get; set; } = "en";
        public int Port { get; set; } = 28630;
        public int DiscoveryPort { get; set; } = 28631;
        public int MaxPlayers { get; set; } = 64;
        public string Motd { get; set; } = string.Empty;
        [JsonPropertyName("features")]
        public ServerFeaturesSettings Features { get; set; } = new ServerFeaturesSettings();
        [JsonPropertyName("moderation")]
        public ServerModerationSettings Moderation { get; set; } = new ServerModerationSettings();
        public string UpdateRuntimeAssetTag { get; set; } = "auto";

        /// <summary>
        /// One of "off", "notify" or "auto". See <see cref="StartupUpdateModes"/>.
        /// Null means the settings file predates this option, which is what lets
        /// <see cref="CheckForUpdatesOnStartup"/> be migrated exactly once.
        /// </summary>
        public string? StartupUpdateMode { get; set; }

        /// <summary>
        /// Blank disables file logging. A bare file name or relative path is resolved
        /// next to the server executable; an absolute path is used as written.
        /// </summary>
        public string LogFile { get; set; } = string.Empty;

        /// <summary>
        /// How much is written, as the comma separated levels the --log-level option takes:
        /// "error", "warning", "info", "debug", or "all". Blank means the normal three, which
        /// is also what a settings file predating this option is given.
        /// </summary>
        public string LogLevel { get; set; } = string.Empty;

        /// <summary>
        /// Superseded by <see cref="StartupUpdateMode"/>. Only read, so that existing
        /// settings files keep their choice; cleared once migrated so it stops being written.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CheckForUpdatesOnStartup { get; set; }
    }
}
