using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using TopSpeed.Localization;
using TopSpeed.Protocol;
using TopSpeed.Server.Commands;
using TopSpeed.Server.Config;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Network;
using TopSpeed.Server.Updates;

namespace TopSpeed.Server
{
    internal static partial class Program
    {
        private static int Main(string[] args)
        {
            LocalizationBootstrap.Configure("en", LocalizationBootstrap.ServerCatalogGroup);

            if (IsHelpRequested(args))
            {
                ShowHelp();
                return 0;
            }

            using var timerResolution = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsTimerResolution(1)
                : null;

            // Settings are read first so a log file configured there can be honoured, using a
            // throwaway logger that only ever writes to the console.
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
            var store = new ServerSettingsStore(settingsPath);
            ServerSettings settings;
            using (var bootstrapLogger = new Logger(LogLevel.All, logFilePath: null, writeToConsole: args.Length > 0))
            {
                settings = store.LoadOrCreate(bootstrapLogger);
            }

            var consoleLoggingEnabled = args.Length > 0;
            var commandLineLogFile = GetArgumentValue(args, "--log-file");
            var useCommandLineLogFile = consoleLoggingEnabled && !string.IsNullOrWhiteSpace(commandLineLogFile);
            var logFile = useCommandLineLogFile
                ? BuildLogFilePath(commandLineLogFile!)
                : string.IsNullOrWhiteSpace(settings.LogFile)
                    ? null
                    : BuildLogFilePath(settings.LogFile);

            // Levels come from the command line when it says anything at all. A log file turned
            // on in settings.json has no levels to go with it, so it records everything.
            var levels = consoleLoggingEnabled
                ? ParseLogLevels(args)
                : logFile != null
                    ? LogLevel.All
                    : LogLevel.None;
            var loggingEnabled = levels != LogLevel.None;
            using var logger = new Logger(
                levels,
                logFile,
                writeToConsole: consoleLoggingEnabled,
                append: !useCommandLineLogFile);
            var serverRelease = $"{ReleaseVersionInfo.ServerYear}.{ReleaseVersionInfo.ServerMonth}.{ReleaseVersionInfo.ServerDay} (r{ReleaseVersionInfo.ServerRevision})";
            if (loggingEnabled)
            {
                logger.Raw(LocalizationService.Format(
                    LocalizationService.Mark("Logging enabled. Levels: {0}. File: {1}."),
                    FormatLogLevels(levels),
                    string.IsNullOrWhiteSpace(logFile)
                        ? LocalizationService.Translate(LocalizationService.Mark("none"))
                        : logFile));
                logger.Raw(LocalizationService.Format(LocalizationService.Mark("Server release: {0}."), serverRelease));
                logger.Raw(LocalizationService.Format(LocalizationService.Mark("Protocol current: {0}. Supported: {1}."), ProtocolProfile.Current, ProtocolProfile.ServerSupported));
                logger.Info(LocalizationService.Mark("TopSpeed Server starting."));
            }

            // The banner still belongs on screen when the log is only going to a file.
            if (!consoleLoggingEnabled)
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("TopSpeed Server starting..."));
                ConsoleSink.WriteLineFormat(LocalizationService.Mark("Server release: {0}"), serverRelease);
                ConsoleSink.WriteLineFormat(LocalizationService.Mark("Protocol version: {0}"), ProtocolProfile.Current);
            }

            LocalizationBootstrap.Configure(settings.Language, LocalizationBootstrap.ServerCatalogGroup);
            ApplyArgumentOverrides(settings, args, logger);
            store.Save(settings, logger);
            var updater = new ServerUpdateRunner(ServerUpdateConfig.Create(settings.UpdateRuntimeAssetTag), logger);

            var config = new RaceServerConfig
            {
                Port = settings.Port,
                DiscoveryPort = settings.DiscoveryPort,
                MaxPlayers = settings.MaxPlayers,
                Motd = settings.Motd,
                Features = settings.Features.Clone(),
                Moderation = settings.Moderation.Clone()
            };
            if (loggingEnabled)
                logger.Info(LocalizationService.Format(
                    LocalizationService.Mark("Server configuration: port={0}, discoveryPort={1}, maxPlayers={2}, features.custom_tracks={3}, features.custom_vehicles={4}, features.text_chat={5}, features.voice_chat={6}, moderation.maxNameLength={7}, moderation.blockRepeatedLettersInName={8}, moderation.allowDuplicateNames={9}."),
                    config.Port,
                    config.DiscoveryPort,
                    config.MaxPlayers,
                    config.Features.CustomTracks,
                    config.Features.CustomVehicles,
                    config.Features.TextChat,
                    config.Features.VoiceChat,
                    config.Moderation.MaxNameLength,
                    config.Moderation.BlockRepeatedLettersInName,
                    config.Moderation.AllowDuplicateNames));

            using var server = new RaceServer(config, logger);
            using var discovery = new ServerDiscoveryService(server, config, logger);
            using var cts = new CancellationTokenSource();
            using var scheduler = new ServerUpdateScheduler(
                server,
                updater,
                logger,
                StartupUpdateModes.Parse(settings.StartupUpdateMode),
                () =>
                {
                    server.ShutdownByHost(LocalizationService.Mark("The server is shutting down to install an update."));
                    cts.Cancel();
                });
            using var commandHost = new CommandHost(server, settings, store, logger, cts, updater, scheduler);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            server.Start();
            discovery.Start();
            commandHost.Start();

            // The update check only runs once the server is accepting players, so a pending
            // update can never keep it from starting.
            scheduler.Start();

            if (!consoleLoggingEnabled)
                ConsoleSink.WriteLine(LocalizationService.Mark("Server started. Press Ctrl+C to stop."));
            RunLoop(server, cts.Token);
            discovery.Stop();
            server.Stop();
            if (loggingEnabled)
                logger.Info(LocalizationService.Mark("TopSpeed Server stopped."));
            if (!consoleLoggingEnabled)
                ConsoleSink.WriteLine(LocalizationService.Mark("Server stopped."));
            return 0;
        }
    }
}




