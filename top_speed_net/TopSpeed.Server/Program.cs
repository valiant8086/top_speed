using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using TopSpeed.Localization;
using TopSpeed.Protocol;
using TopSpeed.Server.Commands;
using TopSpeed.Server.Config;
using TopSpeed.Server.Control;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Network;
using TopSpeed.Server.Service;
using TopSpeed.Server.Updates;

namespace TopSpeed.Server
{
    internal static partial class Program
    {
        private static int Main(string[] args)
        {
            LocalizationBootstrap.Configure("en", LocalizationBootstrap.ServerCatalogGroup);

            // Output and commands go through the session layer from here on, so that both the
            // console and a control connection are served by the same command code. The console
            // session reports itself unreadable when there is no stdin, which is how a server
            // started by a service manager ends up offering its session to whoever attaches.
            // A service has no standard input handle at all, and reading one can block forever
            // rather than reporting end of input. That would wedge the command loop on a
            // console that does not exist, leaving an attached client able to see output but
            // never able to have a command answered. Service mode is known here, so no console
            // session is offered in the first place.
            // True under any service manager, not only Windows. Recorded before anything else
            // because it decides whether a console session is offered and whether the updater
            // may start the program again, and both are wrong if answered late.
            ServiceRuntime.IsRunningAsService = IsServiceMode(args);

            CommandSessions.UseConsoleSession(ServiceRuntime.IsRunningAsService
                ? new HeadlessCommandSession()
                : new ConsoleCommandSession());

            var baseDirectory = AppContext.BaseDirectory;

            // Ahead of the attach check, because these act on the service rather than talk to
            // a running server, and a folder whose server is already running is exactly when
            // stopping or removing it is asked for.
            if (TryGetServiceAction(args, out var serviceAction))
            {
                // The same per action code the menu and the service command reach, with the one
                // thing they can offer and this cannot: something to stop. A copy started with a
                // flag is neither a server nor attached to one, so when a start finds the folder
                // busy it says so and stops there. It could go and tell the running server to
                // shut down, and deliberately does not: a flag in a script or a shortcut must
                // not drop a server full of players as a side effect of asking for a start.
                return ServiceCommands.Execute(serviceAction, baseDirectory, startAutomatically: true);
            }

            // Root is for installing the service and nothing else, and by here that has already
            // been dealt with and returned. A server itself running as root writes its settings,
            // its log, its control socket and its updates into this folder owned by root, which
            // the account that owns the folder then cannot replace. Nothing announces that at the
            // time; it surfaces later as an update that cannot be applied, by which point nobody
            // connects it to how the server was started.
            //
            // Not applied on Windows, where an elevated console is how the service command asks
            // for its rights, and not applied to a service, whose unit may have been edited to
            // run as root by somebody who meant it. Refusing there would leave a machine whose
            // server no longer starts at boot.
            //
            // Nor where root is simply the account there is, which a rented server handed over
            // with root as its only login, or a container, often is. Nothing is lost by allowing
            // it: the harm needs a second account that owns the folder and is then locked out of
            // it, and where none exists there is nobody to lock out.
            if (!OperatingSystem.IsWindows() &&
                !ServiceRuntime.IsRunningAsService &&
                Environment.IsPrivilegedProcess &&
                ServiceIdentity.RootReachedFromAnotherAccount())
            {
                ConsoleSink.WriteLine(Service.ServiceCommands.DoNotRunAsRoot());
                return 1;
            }

            // Somebody running the program in the middle of an update means to attach, and there
            // is nothing to attach to: the server it wants is stopped so that its files can be
            // replaced. Left alone this copy would find no server, start a second one, and take
            // the folder the updater is still writing into and the service is about to want.
            //
            // Leaving is all it can usefully do, and quickly. Its files are locked from the
            // moment the process starts, well before any of this runs, so every moment it stays
            // is a moment the updater may spend failing to replace one of them. Not applied to a
            // service, which is the process coming back afterwards and must never refuse.
            if (!ServiceRuntime.IsRunningAsService &&
                Updates.UpdateMarker.UpdateIsUnderWay(baseDirectory, out var windowComesBackByItself))
            {
                // Different advice for the two, because following the wrong one causes the very
                // collision this is here to prevent: a server the updater is about to open again
                // does not want a second one started by hand first.
                ConsoleSink.WriteLine(windowComesBackByItself
                    ? LocalizationService.Mark("An update is being installed. The server will open again by itself when it is done.")
                    : LocalizationService.Mark("An update is being installed. Run the server again in a moment to attach to it."));
                return 1;
            }

            // Before anything is bound, find out whether this folder already has a server. If
            // it does, this copy becomes a console onto that one rather than a second server
            // quietly claiming the same ports.
            if (!IsExplicitStart(args))
            {
                var attached = ControlClient.Run(baseDirectory);
                if (attached != ControlClientOutcome.NoServerRunning)
                {
                    // Handed over only once the client has returned, so the connection it held
                    // is already dropped and the folder is free for the service to claim.
                    return ServiceRuntime.HandingOverToService
                        ? ServiceConsole.CompleteHandover(baseDirectory)
                        : attached == ControlClientOutcome.SessionEnded ? 0 : 1;
                }

                if (IsAttachRequested(args))
                {
                    ConsoleSink.WriteLine(LocalizationService.Mark("No server is running from this folder."));
                    return 1;
                }
            }

            if (IsHelpRequested(args))
            {
                ShowHelp();
                return 0;
            }

            // A service is always launched by the manager with this argument, which the
            // installer writes into the registration. That is exact, unlike guessing from
            // whether the process happens to look interactive.
            // Only the host is Windows only. Everywhere else the manager runs the program as an
            // ordinary process and watches it, so there is nothing extra to report to.
            if (OperatingSystem.IsWindows() && ServiceRuntime.IsRunningAsService)
                return WindowsServiceHost.Run(args, baseDirectory);

            using var shutdown = new CancellationTokenSource();
            var exitCode = RunServer(args, baseDirectory, shutdown);

            // Here rather than where it was asked for, because this is the first point at which
            // the ports and the endpoint this server held are genuinely released and the service
            // can have them. The window stays and becomes a connection to the service.
            if (ServiceRuntime.HandingOverToService)
                exitCode = ServiceConsole.CompleteHandover(baseDirectory);

            return exitCode;
        }

        /// <summary>
        /// Everything from configuration through to the race loop, split out so a service
        /// manager can run the same body on a thread it is able to ask to stop.
        /// </summary>
        internal static int RunServer(string[] args, string baseDirectory, CancellationTokenSource cts)
        {
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

            // The command line decides how much is logged when it names levels, the setting when
            // it does not, and the usual three when neither says. Naming them on the command line
            // is a thing said about this run; the setting is what a server installed as a service
            // has to be told through, since nobody is there to pass it anything.
            var levels = consoleLoggingEnabled || logFile != null
                ? ParseLogLevels(args) ?? LogLevels.Parse(settings.LogLevel) ?? LogLevels.Default
                : LogLevel.None;
            var loggingEnabled = levels != LogLevel.None;
            using var logger = new Logger(
                levels,
                logFile,
                writeToConsole: consoleLoggingEnabled,
                append: !useCommandLineLogFile);
            if (logger.FileError != null)
                ConsoleSink.WriteLineFormat(
                    LocalizationService.Mark("Could not open the log file {0}: {1}. The server will run without one."),
                    logFile ?? string.Empty,
                    logger.FileError);

            var serverRelease = $"{ReleaseVersionInfo.ServerYear}.{ReleaseVersionInfo.ServerMonth}.{ReleaseVersionInfo.ServerDay} (r{ReleaseVersionInfo.ServerRevision})";
            if (loggingEnabled)
            {
                logger.Raw(LocalizationService.Format(
                    LocalizationService.Mark("Logging enabled. Levels: {0}. File: {1}."),
                    LogLevels.Format(levels),
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

            // Only for a server somebody started themselves. A service has a manager to start it
            // and nobody watching, so a way to start it by hand would be a file with no use and
            // one more thing in the folder.
            if (!ServiceRuntime.IsRunningAsService)
                Service.Launchers.WriteIfMissing(baseDirectory);

            // Named here because both halves are only true here: the port has stopped changing,
            // and the language is loaded.
            ConsoleTitle.Set(LocalizationService.Format(
                LocalizationService.Mark("{0}, port {1}"),
                ConsoleTitle.Product,
                settings.Port));

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

            // Claimed before the network ports are, so that losing the race to another copy
            // means backing out rather than ending up as a second server on the same ports.
            using var control = new ControlListener(baseDirectory, logger, () => DescribeServerStatus(settings));
            if (!control.TryStart())
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("Another server is already running from this folder."));
                return 1;
            }

            // Holding the claim means no update is in progress: the folder is only handed over
            // once the server that had it has gone. Anything left here was left by an updater
            // that did not finish, or by one too old to know to remove it, and leaving it would
            // make every later start wait out a swap that is long over.
            //
            // Worth saying rather than quietly tidying. The updater removes this the moment it
            // finishes, so finding one means the last update stopped partway and the folder may
            // hold some of each version, which is worth knowing before something behaves oddly.
            if (Updates.UpdateMarker.Clear(baseDirectory))
            {
                logger.Warning(LocalizationService.Mark(
                    "An update did not finish, so this folder may hold parts of two versions. Installing the update again will put it right."));
            }

            using var server = new RaceServer(config, logger);
            using var discovery = new ServerDiscoveryService(server, config, logger);
            using var scheduler = new ServerUpdateScheduler(
                server,
                updater,
                logger,
                StartupUpdateModes.Parse(settings.StartupUpdateMode),
                () =>
                {
                    server.ShutdownByHost(LocalizationService.Mark("The server is shutting down to install an update."));
                    cts.Cancel();
                },
                baseDirectory);
            using var commandHost = new CommandHost(server, settings, store, logger, cts, updater, scheduler);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            // Ctrl+C is only reachable when somebody has the console in front of them. Every
            // other way of stopping the server, from a service manager, a container runtime or
            // an updater, sends a termination signal instead. Without these it is killed
            // outright: players are told nothing and Stop never runs.
            using var sigTerm = CreateShutdownSignalHandler(PosixSignal.SIGTERM, cts, logger);
            using var sigInt = CreateShutdownSignalHandler(PosixSignal.SIGINT, cts, logger);

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




