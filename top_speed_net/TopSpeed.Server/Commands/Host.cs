using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TopSpeed.Localization;
using TopSpeed.Protocol;
using TopSpeed.Server.Config;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Network;
using TopSpeed.Server.Localization;
using TopSpeed.Server.Updates;
using TopSpeed.Server.Commands.Options;

namespace TopSpeed.Server.Commands
{
    internal sealed class CommandHost : IDisposable
    {
        private readonly RaceServer _server;
        private readonly ServerSettings _settings;
        private readonly ServerSettingsStore _settingsStore;
        private readonly Logger _logger;
        private readonly CancellationTokenSource _shutdownSource;
        private ServerUpdateRunner _updater;
        private readonly ServerUpdateScheduler _scheduler;
        private readonly CommandRegistry _registry;
        private readonly OptionMenu _serverOptionsMenu;
        private readonly OptionMenu _featureOptionsMenu;
        private readonly OptionMenu _moderationOptionsMenu;
        private Thread? _thread;
        private bool _stopRequested;

        public CommandHost(
            RaceServer server,
            ServerSettings settings,
            ServerSettingsStore settingsStore,
            Logger logger,
            CancellationTokenSource shutdownSource,
            ServerUpdateRunner updater,
            ServerUpdateScheduler scheduler)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _shutdownSource = shutdownSource ?? throw new ArgumentNullException(nameof(shutdownSource));
            _updater = updater ?? throw new ArgumentNullException(nameof(updater));
            _settings.Moderation ??= new ServerModerationSettings();
            _settings.Features ??= new ServerFeaturesSettings();
            _registry = new CommandRegistry(new[]
            {
                new CommandDefinition("help", LocalizationService.Mark("Show available server commands."), ExecuteHelp),
                new CommandDefinition("options", LocalizationService.Mark("Open server options menu."), ExecuteOptions),
                new CommandDefinition("players", LocalizationService.Mark("List connected players and protocol versions."), ExecutePlayers),
                new CommandDefinition("version", LocalizationService.Mark("Display server and protocol versions."), ExecuteVersion),
                new CommandDefinition("update", LocalizationService.Mark("Check for server updates. Add --force to stop waiting and act now."), ExecuteUpdate),
                new CommandDefinition("service", LocalizationService.Mark("Install or control this server as a system service. Add install, uninstall, start, stop, restart or status to skip the menu."), ExecuteService),
                new CommandDefinition("shutdown", LocalizationService.Mark("Shutdown the server."), ExecuteShutdown)
            });
            _featureOptionsMenu = CreateFeatureOptionsMenu();
            _moderationOptionsMenu = CreateModerationOptionsMenu();
            _serverOptionsMenu = CreateServerOptionsMenu();
        }

        public bool Start()
        {
            // The loop now runs even with no console. A server under a service manager has no
            // standard input, but somebody may attach to it later, and the loop is what serves
            // them when they do; it simply waits until a session exists.
            if (IsInputAvailable())
                ConsoleSink.WriteLine(LocalizationService.Mark("Server command interface ready. Type \"help\" to get the list of commands."));
            else
                _logger.Info(LocalizationService.Mark("No console is attached. Server commands are available by attaching to this server."));
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "TopSpeed.Server.Commands"
            };
            _thread.Start();
            return true;
        }

        public void Dispose()
        {
            _stopRequested = true;
        }

        private void RunLoop()
        {
            while (!_stopRequested && !_shutdownSource.IsCancellationRequested)
            {
                if (!CommandInput.TryReadLine(">", out var raw))
                {
                    DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                    return;
                }

                var input = raw.Trim();
                if (input.Length == 0)
                    continue;

                var commandName = ParseCommandName(input);
                if (!_registry.TryGet(commandName, out var command))
                {
                    ConsoleSink.WriteLineFormat(LocalizationService.Mark("Invalid command \"{0}\". Type \"help\" for the list of commands."), commandName);
                    continue;
                }

                try
                {
                    command.Execute(ParseCommandArguments(input));
                }
                catch (Exception ex)
                {
                    _logger.Error(LocalizationService.Format(
                        LocalizationService.Mark("Command '{0}' failed: {1}"),
                        command.Name,
                        ex.Message));
                    ConsoleSink.WriteLine(LocalizationService.Mark("Command failed. Check server logs for details."));
                }
            }
        }

        private void ExecuteHelp()
        {
            ConsoleSink.WriteLine(LocalizationService.Mark("Available commands:"));
            var commands = _registry.Commands;
            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                ConsoleSink.WriteLine(
                    "\"" + command.Name + "\": " + LocalizationService.Translate(command.Description));
            }
        }

        private void ExecutePlayers()
        {
            var players = _server.GetPlayersSnapshot();
            ConsoleSink.WriteLineFormat(LocalizationService.Mark("{0} players are connected:"), players.Length);
            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                ConsoleSink.WriteLineFormat(LocalizationService.Mark("{0}, using protocol version {1}"), player.Name, player.ProtocolVersion);
            }
        }

        private void ExecuteShutdown()
        {
            ConsoleSink.WriteLine(LocalizationService.Mark("shutting down..."));
            _server.ShutdownByHost(LocalizationService.Mark("The server will be shut down immediately by the host."));
            _stopRequested = true;
            _shutdownSource.Cancel();
        }

        private void ExecuteVersion()
        {
            ConsoleSink.WriteLineFormat(LocalizationService.Mark("Server version: {0}"), ServerUpdateConfig.CurrentVersion.ToMachineString());
            ConsoleSink.WriteLineFormat(LocalizationService.Mark("Protocol version: {0}"), ProtocolProfile.Current.ToMachineString());
            ConsoleSink.WriteLineFormat(LocalizationService.Mark("Protocol supported range: {0} to {1}"),
                ProtocolProfile.ServerSupported.MinSupported.ToMachineString(),
                ProtocolProfile.ServerSupported.MaxSupported.ToMachineString());
        }

        private void ExecuteUpdate(string arguments)
        {
            var force = false;
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                if (!string.Equals(arguments.Trim(), "--force", StringComparison.OrdinalIgnoreCase))
                {
                    ConsoleSink.WriteLineFormat(
                        LocalizationService.Mark("Unknown option \"{0}\". The only option is --force."),
                        arguments.Trim());
                    return;
                }

                force = true;
            }

            if (force)
            {
                ExecuteForcedUpdate();
                return;
            }

            // Reporting what is already scheduled rather than starting over is what makes it
            // safe to type update again hours later just to see where things stand.
            var status = _scheduler.GetStatus();
            if (status.State == UpdateSchedulerState.PendingInstall)
            {
                var players = _server.GetPlayersSnapshot().Length;
                ConsoleSink.WriteLineFormat(
                    LocalizationService.Mark("Update {0} is scheduled and will install once the {1} connected players disconnect. Type \"update --force\" to install it now."),
                    status.VersionText,
                    players);
                return;
            }

            if (status.State == UpdateSchedulerState.AwaitingPublication)
            {
                ConsoleSink.WriteLineFormat(
                    LocalizationService.Mark("Version {0} is waiting for its download to be published. The next check is in about {1} minutes. Type \"update --force\" to check now."),
                    status.VersionText,
                    (int)Math.Ceiling(status.TimeUntilNextAttempt.TotalMinutes));
                return;
            }

            // Already seen and not yet asked for, which is where both a typed check and a notify
            // leave things. Typing it again is the asking.
            if (status.State == UpdateSchedulerState.Offered)
            {
                ApproveOffered();
                return;
            }

            RunCheck(installImmediately: false);
        }

        /// <summary>
        /// Takes the offered version and either installs it, if nobody is connected, or leaves it
        /// to go in when the last player leaves.
        /// </summary>
        private void ApproveOffered()
        {
            if (!_scheduler.TryApproveOffered(out var approved) || approved == null)
                return;

            var connected = _server.GetPlayersSnapshot().Length;
            if (connected > 0)
            {
                ConsoleSink.WriteLineFormat(
                    LocalizationService.Mark("Update {0} is scheduled and will install once the {1} connected players disconnect. Type \"update --force\" to install it now."),
                    approved.VersionText,
                    connected);
                return;
            }

            if (_scheduler.TryForceNow(out var readyNow) && readyNow != null)
                _scheduler.InstallNow(readyNow);
        }

        private void ExecuteForcedUpdate()
        {
            if (_scheduler.TryForceNow(out var installNow))
            {
                if (installNow == null)
                {
                    // A re-check was pending and has been brought forward.
                    ConsoleSink.WriteLine(LocalizationService.Mark("Checking for the update download now."));
                    return;
                }

                ConsoleSink.WriteLine(LocalizationService.Mark("Installing the update now. Connected players will be disconnected."));
                _scheduler.InstallNow(installNow);
                return;
            }

            // Nothing found yet, so forcing means find it and then keep going. Every stage this
            // would otherwise stop at is a stage --force is defined as not stopping at.
            RunCheck(installImmediately: true);
        }

        /// <summary>
        /// Checks, and stops at what was asked for: a plain check reports and holds the version
        /// for a second command, while a forced one carries straight on into the install.
        /// </summary>
        private void RunCheck(bool installImmediately)
        {
            if (!_scheduler.TryBeginCheck())
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("An update check is already running. Try again in a moment."));
                return;
            }

            ServerUpdateCheckResult result;
            try
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("Checking for update..."));
                result = _updater.Check();
            }
            finally
            {
                _scheduler.EndCheck();
            }

            switch (result.Outcome)
            {
                case ServerUpdateCheckOutcome.UpToDate:
                    _scheduler.ApplyCheckResult(result, interactive: true);
                    ConsoleSink.WriteLine(LocalizationService.Mark("Server is up-to-date."));
                    return;

                case ServerUpdateCheckOutcome.Failed:
                    _scheduler.ApplyCheckResult(result, interactive: true);
                    ConsoleSink.WriteLine(string.IsNullOrWhiteSpace(result.ErrorMessage)
                        ? LocalizationService.Translate(LocalizationService.Mark("Update check failed."))
                        : result.ErrorMessage);
                    return;

                case ServerUpdateCheckOutcome.NotPublished:
                    // The scheduler prints its own line here, including when the next try is.
                    _scheduler.ApplyCheckResult(result, interactive: true);
                    return;
            }

            if (_scheduler.ApplyCheckResult(result, interactive: true) != CheckFollowUp.ShowChanges ||
                result.Update == null)
                return;

            _updater.WriteChangelog(result.Update);

            if (installImmediately)
            {
                if (_scheduler.TryForceNow(out var readyNow) && readyNow != null)
                {
                    ConsoleSink.WriteLine(LocalizationService.Mark("Installing the update now. Connected players will be disconnected."));
                    _scheduler.InstallNow(readyNow);
                }

                return;
            }

            // Last, after the changes, because it is the part worth remembering and the changes
            // can run to a screenful before it.
            ConsoleSink.WriteLine(LocalizationService.Mark("To update once no players are connected, type update. To update immediately, type update --force."));
        }

        private void ExecuteOptions()
        {
            ShowOptionsMenu(_serverOptionsMenu);
        }

        /// <summary>
        /// Reachable as a command as well as through the options menu, because somebody
        /// attached to a server is answered by their own window here rather than by the server,
        /// and typing one word is a great deal easier than being told to go and find a flag.
        /// </summary>
        private void ExecuteService(string arguments)
        {
            // This server is the one holding the folder, so it is what has to stop before a
            // service can start, it is able to stop itself, and it knows who would be
            // disconnected by that.
            Service.ServiceConsole.Run(
                arguments,
                AppContext.BaseDirectory,
                ExecuteShutdown,
                () => _server.GetPlayersSnapshot().Length);
        }

        private OptionMenu CreateServerOptionsMenu()
        {
            return new OptionMenu(
                LocalizationService.Mark("Server options:"),
                new List<OptionItem>
                {
                    new OptionItem("language", LocalizationService.Mark("Language"), OptionValueType.Choice, EditLanguage, CurrentLanguageLabel),
                    new OptionItem("motd", LocalizationService.Mark("Message of the day"), OptionValueType.Text, EditMotd, () => FormatMotd(_settings.Motd)),
                    new OptionItem("server_port", LocalizationService.Mark("Server port"), OptionValueType.Numeric, EditServerPort, () => _settings.Port.ToString()),
                    new OptionItem("discovery_port", LocalizationService.Mark("Discovery port"), OptionValueType.Numeric, EditDiscoveryPort, () => _settings.DiscoveryPort.ToString()),
                    new OptionItem("max_players", LocalizationService.Mark("Max players"), OptionValueType.Numeric, EditMaxPlayers, () => _settings.MaxPlayers.ToString()),
                    new OptionItem("features", LocalizationService.Mark("Features"), OptionValueType.Menu, () => ShowOptionsMenu(_featureOptionsMenu)),
                    new OptionItem("server_architecture", LocalizationService.Mark("Server architecture"), OptionValueType.Choice, EditRuntimeArchitecture, CurrentRuntimeAssetLabel),
                    new OptionItem("startup_update_mode", LocalizationService.Mark("Update checking"), OptionValueType.Choice, EditStartupUpdateMode, CurrentStartupUpdateModeLabel),
                    new OptionItem("log_file", LocalizationService.Mark("Log file"), OptionValueType.Text, EditLogFile, () => FormatLogFile(_settings.LogFile)),
                    new OptionItem("log_level", LocalizationService.Mark("Log level"), OptionValueType.Choice, EditLogLevel, () => LogLevels.Normalize(_settings.LogLevel)),
                    // No service entry here on purpose. Everything in this menu is a setting of
                    // this server, kept in its settings file. Installing or starting a service
                    // is an instruction to the host system about how the server gets launched,
                    // and it has to be carried out by a process a person launched, which the
                    // server answering a menu selection may well not be. It lives on the
                    // "service" command instead, where the window somebody typed into can
                    // recognise the word and keep it.
                    new OptionItem("moderation", LocalizationService.Mark("Moderation"), OptionValueType.Menu, () => ShowOptionsMenu(_moderationOptionsMenu))
                });
        }

        private OptionMenu CreateFeatureOptionsMenu()
        {
            return new OptionMenu(
                LocalizationService.Mark("Feature options:"),
                new List<OptionItem>
                {
                    new OptionItem("custom_tracks", LocalizationService.Mark("Custom tracks"), OptionValueType.Bool, ToggleCustomTracks, () => CommandInput.FormatOnOff(_settings.Features.CustomTracks)),
                    new OptionItem("custom_vehicles", LocalizationService.Mark("Custom vehicles"), OptionValueType.Bool, ToggleCustomVehicles, () => CommandInput.FormatOnOff(_settings.Features.CustomVehicles)),
                    new OptionItem("text_chat", LocalizationService.Mark("Text chat"), OptionValueType.Bool, ToggleTextChat, () => CommandInput.FormatOnOff(_settings.Features.TextChat)),
                    new OptionItem("voice_chat", LocalizationService.Mark("Voice chat"), OptionValueType.Bool, ToggleVoiceChat, () => CommandInput.FormatOnOff(_settings.Features.VoiceChat))
                });
        }

        private OptionMenu CreateModerationOptionsMenu()
        {
            return new OptionMenu(
                LocalizationService.Mark("Moderation options:"),
                new List<OptionItem>
                {
                    new OptionItem("block_repeated_letters_in_name", LocalizationService.Mark("Block repeated letters in call signs"), OptionValueType.Bool, ToggleBlockRepeatedLettersInName, () => CommandInput.FormatOnOff(_settings.Moderation.BlockRepeatedLettersInName)),
                    new OptionItem("max_name_length", LocalizationService.Mark("Maximum call sign length"), OptionValueType.Numeric, EditModerationMaxNameLength, () => _settings.Moderation.MaxNameLength.ToString()),
                    new OptionItem("allow_duplicate_names", LocalizationService.Mark("Allow duplicate call signs"), OptionValueType.Bool, ToggleAllowDuplicateNames, () => CommandInput.FormatOnOff(_settings.Moderation.AllowDuplicateNames))
                });
        }

        private void ShowOptionsMenu(OptionMenu menu)
        {
            if (menu == null)
                return;

            while (!_stopRequested && !_shutdownSource.IsCancellationRequested)
            {
                var options = BuildOptionMenuEntries(menu);
                var backIndex = menu.Items.Count;
                if (!CommandInput.TryPromptMenuChoice(menu.TitleMessageId, options, out var choiceIndex, backOptionIndex: backIndex))
                {
                    DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                    return;
                }

                if (choiceIndex == backIndex || choiceIndex < 0 || choiceIndex >= menu.Items.Count)
                    return;

                menu.Items[choiceIndex].Activate();
            }
        }

        private static IReadOnlyList<string> BuildOptionMenuEntries(OptionMenu menu)
        {
            var entries = new List<string>(menu.Items.Count + 1);
            for (var i = 0; i < menu.Items.Count; i++)
            {
                var item = menu.Items[i];
                var label = LocalizationService.Translate(item.LabelMessageId);
                if (item.Type == OptionValueType.Menu)
                {
                    entries.Add(label);
                    continue;
                }

                entries.Add(label + ": " + item.GetValueOrEmpty());
            }

            entries.Add(LocalizationService.Translate(LocalizationService.Mark("Back")));
            return entries;
        }

        private string CurrentLanguageLabel()
        {
            var languages = ServerLanguages.Load();
            return ServerLanguages.ResolveDisplayLabel(_settings.Language, languages);
        }

        private string CurrentRuntimeAssetLabel()
        {
            return ServerUpdateConfig.ResolveCurrentRuntimeLabel(_settings.UpdateRuntimeAssetTag);
        }

        private void EditLanguage()
        {
            var languages = ServerLanguages.Load();
            if (languages.Count == 0)
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("No languages are available."));
                return;
            }

            var options = new List<string>(languages.Count + 1);
            for (var i = 0; i < languages.Count; i++)
                options.Add(languages[i].ListLabel);
            options.Add(LocalizationService.Translate(LocalizationService.Mark("Back")));

            if (!CommandInput.TryPromptMenuChoice(LocalizationService.Mark("Choose server language:"), options, out var choiceIndex, backOptionIndex: options.Count - 1))
            {
                DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                return;
            }

            if (choiceIndex < 0 || choiceIndex >= languages.Count)
                return;

            var selected = languages[choiceIndex];
            var resolvedCode = ServerLanguages.ResolveCode(selected.Code, languages);
            var changed = !string.Equals(_settings.Language, resolvedCode, StringComparison.OrdinalIgnoreCase);
            _settings.Language = resolvedCode;
            LocalizationBootstrap.Configure(_settings.Language, LocalizationBootstrap.ServerCatalogGroup);
            SaveSettings();

            if (changed)
            {
                ConsoleSink.WriteLineFormat(LocalizationService.Mark("Server language set to {0}."), selected.ListLabel);
                return;
            }

            ConsoleSink.WriteLineFormat(LocalizationService.Mark("Server language remains {0}."), selected.ListLabel);
        }

        private void EditRuntimeArchitecture()
        {
            var runtimeOptions = ServerUpdateConfig.GetRuntimeOptions();
            var options = new List<string>(runtimeOptions.Count + 1);
            for (var i = 0; i < runtimeOptions.Count; i++)
                options.Add(ServerUpdateConfig.FormatRuntimeOptionLabel(runtimeOptions[i]));
            options.Add(LocalizationService.Translate(LocalizationService.Mark("Back")));

            if (!CommandInput.TryPromptMenuChoice(LocalizationService.Mark("Choose server architecture:"), options, out var choiceIndex, backOptionIndex: options.Count - 1))
            {
                DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                return;
            }

            if (choiceIndex < 0 || choiceIndex >= runtimeOptions.Count)
                return;

            var selected = runtimeOptions[choiceIndex];
            var changed = !string.Equals(_settings.UpdateRuntimeAssetTag, selected.ShortName, StringComparison.OrdinalIgnoreCase);
            _settings.UpdateRuntimeAssetTag = selected.ShortName;
            SaveSettings();
            _updater = new ServerUpdateRunner(ServerUpdateConfig.Create(_settings.UpdateRuntimeAssetTag), _logger);

            if (changed)
            {
                ConsoleSink.WriteLine(LocalizationService.Format(
                    LocalizationService.Mark("Server architecture set to {0}."),
                    ServerUpdateConfig.FormatRuntimeOptionLabel(selected)));
                return;
            }

            ConsoleSink.WriteLine(LocalizationService.Format(
                LocalizationService.Mark("Server architecture remains {0}."),
                ServerUpdateConfig.FormatRuntimeOptionLabel(selected)));
        }

        private void EditMotd()
        {
            var prompt = LocalizationService.Format(
                LocalizationService.Mark("Enter message of the day (max {0} chars, empty clears value):"),
                ProtocolConstants.MaxMotdLength);
            if (!CommandInput.TryPromptText(prompt, ProtocolConstants.MaxMotdLength, allowEmpty: true, out var motd))
            {
                DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                return;
            }

            _settings.Motd = motd;
            _server.SetMotd(motd);
            SaveSettings();
            ConsoleSink.WriteLine(LocalizationService.Mark("Message of the day updated."));
        }

        private void EditServerPort()
        {
            if (!CommandInput.TryPromptInt(LocalizationService.Mark("Enter server port (1-65535):"), 1, 65535, out var port))
            {
                DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                return;
            }

            _settings.Port = port;
            SaveSettings();
            ConsoleSink.WriteLineFormat(LocalizationService.Mark("Server port updated to {0}. Restart required for this change."), port);
        }

        private void EditDiscoveryPort()
        {
            if (!CommandInput.TryPromptInt(LocalizationService.Mark("Enter discovery port (1-65535):"), 1, 65535, out var port))
            {
                DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                return;
            }

            _settings.DiscoveryPort = port;
            SaveSettings();
            ConsoleSink.WriteLineFormat(LocalizationService.Mark("Discovery port updated to {0}. Restart required for this change."), port);
        }

        private void EditMaxPlayers()
        {
            if (!CommandInput.TryPromptInt(LocalizationService.Mark("Enter max players (1-255):"), 1, byte.MaxValue, out var maxPlayers))
            {
                DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                return;
            }

            _settings.MaxPlayers = maxPlayers;
            _server.SetMaxPlayers(maxPlayers);
            SaveSettings();
            ConsoleSink.WriteLineFormat(LocalizationService.Mark("Max players updated to {0}."), maxPlayers);
        }

        private void ToggleCustomTracks()
        {
            _settings.Features.CustomTracks = !_settings.Features.CustomTracks;
            ApplyFeatureSettings();
            ConsoleSink.WriteLine(BuildOptionLine(LocalizationService.Mark("Custom tracks"), CommandInput.FormatOnOff(_settings.Features.CustomTracks)));
        }

        private void ToggleCustomVehicles()
        {
            _settings.Features.CustomVehicles = !_settings.Features.CustomVehicles;
            ApplyFeatureSettings();
            ConsoleSink.WriteLine(BuildOptionLine(LocalizationService.Mark("Custom vehicles"), CommandInput.FormatOnOff(_settings.Features.CustomVehicles)));
        }

        private string CurrentStartupUpdateModeLabel()
        {
            return LocalizationService.Translate(DescribeStartupUpdateMode(_settings.StartupUpdateMode));
        }

        private static string DescribeStartupUpdateMode(string? mode)
        {
            return StartupUpdateModes.Parse(mode) switch
            {
                StartupUpdateMode.Notify => LocalizationService.Mark("notify: say when an update is available"),
                StartupUpdateMode.Auto => LocalizationService.Mark("auto: install updates when no players are connected"),
                _ => LocalizationService.Mark("off: never check")
            };
        }

        private void EditStartupUpdateMode()
        {
            var modes = StartupUpdateModes.All;
            var options = new List<string>(modes.Length + 1);
            for (var i = 0; i < modes.Length; i++)
                options.Add(LocalizationService.Translate(DescribeStartupUpdateMode(modes[i])));
            options.Add(LocalizationService.Translate(LocalizationService.Mark("Back")));

            if (!CommandInput.TryPromptMenuChoice(
                    LocalizationService.Mark("Choose when the server checks for updates:"),
                    options,
                    out var choiceIndex,
                    backOptionIndex: options.Count - 1))
            {
                DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                return;
            }

            if (choiceIndex < 0 || choiceIndex >= modes.Length)
                return;

            _settings.StartupUpdateMode = modes[choiceIndex];
            SaveSettings();
            ConsoleSink.WriteLine(BuildOptionLine(LocalizationService.Mark("Update checking"), CurrentStartupUpdateModeLabel()));
            ConsoleSink.WriteLine(LocalizationService.Mark("Restart required for this change."));
        }

        private static string FormatLogFile(string? logFile)
        {
            return string.IsNullOrWhiteSpace(logFile)
                ? LocalizationService.Translate(LocalizationService.Mark("(off)"))
                : logFile;
        }

        private void EditLogFile()
        {
            // Said before the prompt, because it is advice about what to type. Nothing checks the
            // answer against it: a server that is never installed as a service may keep its log
            // wherever its owner likes, and the one place that always works is worth knowing
            // before choosing rather than being refused afterwards.
            ConsoleSink.WriteLine(LocalizationService.Mark(
                "A log kept inside the server's own folder is recommended. A server installed as a service runs as a limited account that can write there and nowhere else, so a log anywhere outside that folder would go unwritten."));

            if (!CommandInput.TryPromptText(
                    LocalizationService.Mark("Enter a log file name or path, or leave blank to turn logging off:"),
                    260,
                    allowEmpty: true,
                    out var logFile))
            {
                DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                return;
            }

            _settings.LogFile = logFile;
            SaveSettings();
            ConsoleSink.WriteLine(BuildOptionLine(LocalizationService.Mark("Log file"), FormatLogFile(_settings.LogFile)));
            if (!string.IsNullOrWhiteSpace(logFile))
                ConsoleSink.WriteLine(LocalizationService.Mark("A name or relative path is written next to the server program; an absolute path is used as written."));
            ConsoleSink.WriteLine(LocalizationService.Mark("Restart required for this change. The --log-file and log level command line options override this setting. See the server documentation for details."));
        }

        private void EditLogLevel()
        {
            var presets = LogLevels.Presets;
            var options = new List<string>(presets.Length + 1);
            for (var i = 0; i < presets.Length; i++)
                options.Add(LogLevels.Normalize(presets[i]));
            options.Add(LocalizationService.Translate(LocalizationService.Mark("Back")));

            if (!CommandInput.TryPromptMenuChoice(
                    LocalizationService.Mark("Choose which levels are logged. These are the same levels --log-level takes, and error, warning and info is the usual choice; debug adds detail meant for diagnosing problems."),
                    options,
                    out var choiceIndex,
                    backOptionIndex: options.Count - 1))
            {
                DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                return;
            }

            if (choiceIndex < 0 || choiceIndex >= presets.Length)
                return;

            _settings.LogLevel = LogLevels.Normalize(presets[choiceIndex]);
            SaveSettings();
            ConsoleSink.WriteLine(BuildOptionLine(LocalizationService.Mark("Log level"), LogLevels.Normalize(_settings.LogLevel)));
            ConsoleSink.WriteLine(LocalizationService.Mark("Restart required for this change. The log level command line options override this setting."));
        }

        private void ToggleBlockRepeatedLettersInName()
        {
            _settings.Moderation.BlockRepeatedLettersInName = !_settings.Moderation.BlockRepeatedLettersInName;
            ApplyModerationSettings();
            ConsoleSink.WriteLine(BuildOptionLine(LocalizationService.Mark("Block repeated letters in call signs"), CommandInput.FormatOnOff(_settings.Moderation.BlockRepeatedLettersInName)));
        }

        private void EditModerationMaxNameLength()
        {
            if (!CommandInput.TryPromptInt(
                    LocalizationService.Format(LocalizationService.Mark("Enter max call sign length (1-{0}):"), ProtocolConstants.MaxPlayerNameLength),
                    1,
                    ProtocolConstants.MaxPlayerNameLength,
                    out var maxNameLength))
            {
                DisableCommands(LocalizationService.Mark("Standard input is no longer available. Server commands are disabled."));
                return;
            }

            _settings.Moderation.MaxNameLength = maxNameLength;
            ApplyModerationSettings();
            ConsoleSink.WriteLineFormat(LocalizationService.Mark("Maximum call sign length updated to {0}."), maxNameLength);
        }

        private void ToggleAllowDuplicateNames()
        {
            _settings.Moderation.AllowDuplicateNames = !_settings.Moderation.AllowDuplicateNames;
            ApplyModerationSettings();
            ConsoleSink.WriteLine(BuildOptionLine(LocalizationService.Mark("Allow duplicate call signs"), CommandInput.FormatOnOff(_settings.Moderation.AllowDuplicateNames)));
        }

        private void ToggleTextChat()
        {
            _settings.Features.TextChat = !_settings.Features.TextChat;
            ApplyFeatureSettings();
            ConsoleSink.WriteLine(BuildOptionLine(LocalizationService.Mark("Text chat"), CommandInput.FormatOnOff(_settings.Features.TextChat)));
        }

        private void ToggleVoiceChat()
        {
            _settings.Features.VoiceChat = !_settings.Features.VoiceChat;
            ApplyFeatureSettings();
            ConsoleSink.WriteLine(BuildOptionLine(LocalizationService.Mark("Voice chat"), CommandInput.FormatOnOff(_settings.Features.VoiceChat)));
        }

        private void ApplyFeatureSettings()
        {
            _server.SetFeatureSettings(_settings.Features);
            SaveSettings();
        }

        private void ApplyModerationSettings()
        {
            _server.SetModerationSettings(_settings.Moderation);
            SaveSettings();
        }

        private void SaveSettings()
        {
            _settingsStore.Save(_settings, _logger);
        }

        private void DisableCommands(string message)
        {
            _stopRequested = true;
            _logger.Warning(message);
            ConsoleSink.WriteLine(message);
        }

        private static string ParseCommandName(string input)
        {
            var index = input.IndexOf(' ');
            if (index < 0)
                return input.Trim();
            return input.Substring(0, index).Trim();
        }

        private static string ParseCommandArguments(string input)
        {
            var index = input.IndexOf(' ');
            if (index < 0)
                return string.Empty;
            return input.Substring(index + 1).Trim();
        }

        private static string FormatMotd(string motd)
        {
            return string.IsNullOrWhiteSpace(motd)
                ? LocalizationService.Translate(LocalizationService.Mark("(empty)"))
                : motd;
        }

        private static bool IsInputAvailable()
        {
            return ConsoleCommandSession.IsInputAvailable();
        }

        private static string BuildOptionLine(string labelMessageId, string value)
        {
            var label = LocalizationService.Translate(labelMessageId);
            var safeValue = value ?? string.Empty;
            return label + ": " + safeValue;
        }
    }
}




