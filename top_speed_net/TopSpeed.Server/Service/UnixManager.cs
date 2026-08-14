using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using TopSpeed.Localization;
using TopSpeed.Server.Control;

namespace TopSpeed.Server.Service
{
    /// <summary>
    /// Installs this folder's server with systemd or launchd, two ways round.
    ///
    /// Run with root already in hand, it does the whole thing: writes the unit where the system
    /// keeps them and tells the manager to load it. Run without, it cannot, and writing a file
    /// into a directory only root owns is not something to fail at halfway. So it writes the unit
    /// beside the server along with a short script that installs it, and says to read them and
    /// run the script. The script asks for the password itself, which means one prompt and
    /// nothing to retype: every path in it is already there, spelled correctly and quoted.
    ///
    /// Both routes end in the same place. Which one somebody wants is answered by whether they
    /// typed sudo, rather than by anything this has to ask.
    /// </summary>
    internal sealed class UnixServiceManager : IServiceManager
    {
        public ServiceStatus Query(string directory)
        {
            // Asking systemd or launchd would mean running them, and their answer is only ever
            // wanted by a person, who has the command for it in front of them.
            return new ServiceStatus(ServiceInstallState.Unsupported, UnitNameFor(directory), false);
        }

        public ServiceActionResult Install(string directory, bool startAutomatically)
        {
            if (ServiceIdentity.IsProtectedLocation(directory, out var location))
            {
                return ServiceActionResult.Failed(LocalizationService.Format(
                    LocalizationService.Mark("This server cannot be installed as a service from {0}. The server updates itself in place, so it must run from a folder it can write to, and giving a service write access inside a protected location would let anything that can write there run as that service later. Move the server folder somewhere else, such as a folder you created yourself, and install it from there."),
                    location ?? string.Empty));
            }

            try
            {
                return Environment.IsPrivilegedProcess
                    ? InstallNow(directory)
                    : WriteInstaller(directory);
            }
            catch (IOException ex)
            {
                return ServiceActionResult.Failed(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return ServiceActionResult.Failed(ex.Message);
            }
        }

        public ServiceActionResult Uninstall(string directory)
        {
            try
            {
                return Environment.IsPrivilegedProcess
                    ? UninstallNow(directory)
                    : WriteRemover(directory);
            }
            catch (IOException ex)
            {
                return ServiceActionResult.Failed(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return ServiceActionResult.Failed(ex.Message);
            }
        }

        public ServiceActionResult Start(string directory)
        {
            return OneCommand(
                directory,
                LocalizationService.Mark("To start it, run:\n{0}"),
                LocalizationService.Mark("The service is running."),
                OperatingSystem.IsMacOS()
                    ? new[] { "kickstart", "-k", "system/" + UnitNameFor(directory) }
                    : new[] { "start", UnitNameFor(directory) });
        }

        public ServiceActionResult Stop(string directory)
        {
            return OneCommand(
                directory,
                LocalizationService.Mark("To stop it, run:\n{0}"),
                LocalizationService.Mark("The service is stopped."),
                OperatingSystem.IsMacOS()
                    ? new[] { "bootout", "system/" + UnitNameFor(directory) }
                    : new[] { "stop", UnitNameFor(directory) });
        }

        /// <summary>
        /// One command, because both of these systems have one. Telling somebody to stop it and
        /// then start it would be two chances to be interrupted between the halves, and neither
        /// system needs it done that way.
        /// </summary>
        public ServiceActionResult Restart(string directory)
        {
            return OneCommand(
                directory,
                LocalizationService.Mark("To restart it, run:\n{0}"),
                LocalizationService.Mark("The service is running."),
                OperatingSystem.IsMacOS()
                    ? new[] { "kickstart", "-k", "system/" + UnitNameFor(directory) }
                    : new[] { "restart", UnitNameFor(directory) });
        }

        /// <summary>
        /// Runs one manager command when root is already held, and otherwise says what to type.
        /// These are the three that need no file written, so there is nothing to read first and
        /// a script would be a file to run instead of a line to run, which is not an improvement.
        /// </summary>
        private static ServiceActionResult OneCommand(
            string directory,
            string instruction,
            string doneMessage,
            string[] arguments)
        {
            if (!Environment.IsPrivilegedProcess)
            {
                return ServiceActionResult.Ok(LocalizationService.Format(
                    instruction,
                    Commands(Sudo(ManagerProgram(), arguments))));
            }

            return Run(ManagerProgram(), arguments, out var output)
                ? ServiceActionResult.Ok(LocalizationService.Translate(doneMessage))
                : ServiceActionResult.Failed(output);
        }

        /// <summary>
        /// The whole install, root already in hand.
        ///
        /// The unit goes straight to where the system keeps them rather than beside the server
        /// first. Writing it into the server folder as root would leave a file there its owner
        /// could not later delete, and the only reason to put it there at all is so somebody can
        /// read it before agreeing, which is a step whoever typed sudo has already skipped.
        /// </summary>
        private ServiceActionResult InstallNow(string directory)
        {
            if (!TryOwningUser(out var owner, out var refusal))
                return ServiceActionResult.Failed(refusal);

            var name = UnitNameFor(directory);
            var systemPath = SystemUnitPath(name);
            File.WriteAllText(systemPath, UnitTextFor(directory, owner));

            foreach (var command in LoadCommands(name, systemPath))
            {
                if (!Run(ManagerProgram(), command, out var output))
                    return ServiceActionResult.Failed(output);
            }

            return ServiceActionResult.Ok(LocalizationService.Format(
                LocalizationService.Mark("Installed the service {0}, running as {1}. It starts with the machine."),
                name,
                owner));
        }

        private ServiceActionResult UninstallNow(string directory)
        {
            var name = UnitNameFor(directory);
            var systemPath = SystemUnitPath(name);

            // Told to forget it before the file goes, and not failed over: a service already
            // unloaded, or never loaded, answers this with an error and is in exactly the state
            // being asked for. What matters is whether the registration is gone afterwards.
            foreach (var command in UnloadCommands(name))
                Run(ManagerProgram(), command, out _);

            if (File.Exists(systemPath))
                File.Delete(systemPath);

            foreach (var command in ReloadCommands())
                Run(ManagerProgram(), command, out _);

            return ServiceActionResult.Ok(LocalizationService.Format(
                LocalizationService.Mark("Removed the service {0}. The server folder was left alone."),
                name));
        }

        /// <summary>
        /// The unit beside the server, and a script that installs it. Named so that the one to
        /// run is obvious from its name alone, and marked runnable here, since the thing that
        /// most often goes wrong with a shipped script is that it arrived without that.
        /// </summary>
        private ServiceActionResult WriteInstaller(string directory)
        {
            var folder = ServiceIdentity.DisplayPath(directory);
            var name = UnitNameFor(directory);
            var unitPath = Path.Combine(folder, UnitFileName(name));
            var systemPath = SystemUnitPath(name);

            File.WriteAllText(unitPath, UnitTextFor(directory, ServiceIdentity.OwningUserName()));

            var steps = new StringBuilder();
            steps.Append(Line(Sudo("cp", new[] { unitPath, systemPath })));
            foreach (var command in LoadCommands(name, systemPath))
                steps.Append(Line(Sudo(ManagerProgram(), command)));

            var scriptPath = Path.Combine(folder, ScriptName("install-service"));
            WriteScript(
                scriptPath,
                "Installing this folder's TopSpeed server as a system service.",
                steps.ToString(),
                "Done. The server will start with the machine from now on.",
                stopOnError: true);

            return ServiceActionResult.Ok(LocalizationService.Format(
                LocalizationService.Mark("Wrote {0}, which describes the service, and {1}, which installs it. Read them if you like, then run {1}. It asks for your password and does the rest."),
                unitPath,
                scriptPath));
        }

        private ServiceActionResult WriteRemover(string directory)
        {
            var folder = ServiceIdentity.DisplayPath(directory);
            var name = UnitNameFor(directory);
            var systemPath = SystemUnitPath(name);

            var steps = new StringBuilder();
            foreach (var command in UnloadCommands(name))
                steps.Append(Line(Sudo(ManagerProgram(), command) + " || true"));
            steps.Append(Line(Sudo("rm", new[] { "-f", systemPath })));
            foreach (var command in ReloadCommands())
                steps.Append(Line(Sudo(ManagerProgram(), command)));

            // Everything this folder gained by being installed goes with it, this script last of
            // all. A remover that outlives what it removed is a way to remove it again, and the
            // unit beside the server is worth no more than the registration it made.
            var scriptPath = Path.Combine(folder, ScriptName("uninstall-service"));
            steps.Append(Line("rm -f " + Quote(Path.Combine(folder, UnitFileName(name)))));
            steps.Append(Line("rm -f " + Quote(Path.Combine(folder, ScriptName("install-service")))));
            steps.Append(Line("rm -f \"$0\""));

            WriteScript(
                scriptPath,
                "Removing this folder's TopSpeed server from the system's services.",
                steps.ToString(),
                "Done. The service is gone and the server folder was left alone.",
                stopOnError: false);

            return ServiceActionResult.Ok(LocalizationService.Format(
                LocalizationService.Mark("Wrote {0}, which removes the service. Read it if you like, then run it. It asks for your password, and clears itself away afterwards."),
                scriptPath));
        }

        /// <summary>
        /// A script that says what it is doing as it goes. Both commands in it are silent when
        /// they work, so without this it would run for a few seconds and finish without a word,
        /// which reads as nothing having happened.
        /// </summary>
        private static void WriteScript(string path, string purpose, string steps, string ending, bool stopOnError)
        {
            var script = new StringBuilder();
            script.Append("#!/bin/sh\n");
            script.Append("# ").Append(purpose).Append('\n');
            script.Append("# Written by the TopSpeed server. Nothing happens until you run it.\n\n");
            // Installing stops at the first thing that fails, because every step after one is
            // about a file that was not copied. Removing does not: each step is a different
            // thing to be rid of, and one already gone is no reason to keep the rest.
            if (stopOnError)
                script.Append("set -e\n\n");
            script.Append("echo \"").Append(purpose).Append("\"\n");
            script.Append(steps);
            script.Append("echo \"").Append(ending).Append("\"\n");

            File.WriteAllText(path, script.ToString());

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        private static string Line(string command)
        {
            return command + "\n";
        }

        /// <summary>
        /// The account the service is to run as, which is never root.
        ///
        /// A server running as root writes root owned files into a folder whose owner then cannot
        /// update it, so the one account this must not choose is the one it is running as. Under
        /// sudo the invoking account is knowable and is the right answer; logged in as root there
        /// is nothing to fall back to and nothing worth guessing.
        /// </summary>
        private static bool TryOwningUser(out string owner, out string refusal)
        {
            owner = ServiceIdentity.OwningUserName();
            refusal = string.Empty;

            if (!string.Equals(owner, "root", StringComparison.Ordinal))
                return true;

            refusal = LocalizationService.Translate(LocalizationService.Mark(
                "This has to know which account the service will run as, and it cannot be root: a server running as root leaves files behind that its own owner cannot replace when it updates. Run it with sudo from the account that owns the server folder rather than logged in as root."));
            return false;
        }

        private static string ManagerProgram()
        {
            return OperatingSystem.IsMacOS() ? "launchctl" : "systemctl";
        }

        private static string SystemUnitPath(string name)
        {
            return OperatingSystem.IsMacOS()
                ? "/Library/LaunchDaemons/" + name + ".plist"
                : "/etc/systemd/system/" + name;
        }

        private static string UnitFileName(string name)
        {
            return OperatingSystem.IsMacOS() ? name + ".plist" : name;
        }

        private static string ScriptName(string stem)
        {
            // launchd's platform gives a double clickable extension and systemd's does not, which
            // is the whole of the difference: Finder runs a .command in Terminal, and no Linux
            // file manager agrees on how to run anything, so there it is a shell script named
            // like one.
            return OperatingSystem.IsMacOS() ? stem + ".command" : stem + ".sh";
        }

        private static string[][] LoadCommands(string name, string systemPath)
        {
            return OperatingSystem.IsMacOS()
                ? new[] { new[] { "bootstrap", "system", systemPath } }
                : new[] { new[] { "daemon-reload" }, new[] { "enable", "--now", name } };
        }

        private static string[][] UnloadCommands(string name)
        {
            return OperatingSystem.IsMacOS()
                ? new[] { new[] { "bootout", "system/" + name } }
                : new[] { new[] { "disable", "--now", name } };
        }

        private static string[][] ReloadCommands()
        {
            return OperatingSystem.IsMacOS()
                ? Array.Empty<string[]>()
                : new[] { new[] { "daemon-reload" } };
        }

        private static string UnitTextFor(string directory, string owner)
        {
            return OperatingSystem.IsMacOS()
                ? BuildLaunchdPlist(directory, owner)
                : BuildSystemdUnit(directory, owner);
        }

        /// <summary>
        /// A command as it would be typed, with every path quoted.
        ///
        /// Quoted without asking whether it needs to be. A folder with a space in its name is
        /// ordinary on a Mac and made this command silently wrong, in a way whose error message
        /// says nothing about spaces: cp is handed three arguments and complains that the last is
        /// not a directory.
        /// </summary>
        private static string Sudo(string program, string[] arguments)
        {
            var line = new StringBuilder("sudo ").Append(program);
            foreach (var argument in arguments)
                line.Append(' ').Append(NeedsQuoting(argument) ? Quote(argument) : argument);

            return line.ToString();
        }

        private static bool NeedsQuoting(string argument)
        {
            // Anything naming a place, and nothing naming an option or a subcommand. Quoting a
            // verb would be harmless, and leaving a path unquoted is what went wrong.
            return argument.Contains('/', StringComparison.Ordinal) || argument.Contains(' ', StringComparison.Ordinal);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        /// <summary>
        /// Commands as they are to be typed, indented under whatever sentence introduces them.
        ///
        /// They are built here and handed to a message rather than written inside one. A program
        /// name is not a word: sudo, systemctl and launchctl are spelled the same in every
        /// language, and there is no translation of /etc/systemd/system. Carrying them into a
        /// translated string is what would let one come back rewritten, in a form nobody here
        /// could check and only a stranger's machine would refuse.
        /// </summary>
        private static string Commands(params string[] lines)
        {
            return "  " + string.Join("\n  ", lines);
        }

        private static bool Run(string program, string[] arguments, out string output)
        {
            var info = new ProcessStartInfo
            {
                FileName = program,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                info.ArgumentList.Add(argument);

            try
            {
                using var process = Process.Start(info);
                if (process == null)
                {
                    output = program;
                    return false;
                }

                var text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                output = string.IsNullOrWhiteSpace(text) ? program : text.Trim();
                return process.ExitCode == 0;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                output = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// The unit text, built apart from writing it so it can be checked without a machine
        /// that runs systemd. Nobody here has one, and the parts of it that matter are exactly
        /// the parts that are silently wrong if they are missing.
        /// </summary>
        public static string BuildSystemdUnit(string directory)
        {
            return BuildSystemdUnit(directory, ServiceIdentity.OwningUserName());
        }

        public static string BuildSystemdUnit(string directory, string owner)
        {
            var folder = ServiceIdentity.DisplayPath(directory);

            var unit = new StringBuilder();
            unit.Append("[Unit]\n");
            unit.Append("Description=").Append(ServiceIdentity.DisplayNameFor(directory)).Append('\n');
            // Not merely "network", which is satisfied before an address exists.
            unit.Append("Wants=network-online.target\n");
            unit.Append("After=network-online.target\n\n");
            unit.Append("[Service]\n");
            unit.Append("Type=simple\n");
            // Waits out an update that is still being written before the new server is started
            // on a folder that is half of each version. Run from WorkingDirectory below, so the
            // name needs no path and no quoting whatever the folder is called; the doubled
            // dollars are how a literal one survives systemd reading the line.
            //
            // Bounded, because the file is removed by the updater and an updater older than it
            // never learns to: without a limit one of those would stop the server starting at
            // all, which is far worse than the minute it costs to give up and start anyway.
            unit.Append("ExecStartPre=/bin/sh -c 'i=0; while [ -e ")
                .Append(Updates.UpdateMarker.FileName)
                .Append(" ] && [ $$i -lt 60 ]; do sleep 1; i=$$((i+1)); done'\n");
            // The argument matters as much as the path. It tells the server something else is
            // managing it, which is what stops the updater from launching a second copy behind
            // this unit's back after an update.
            unit.Append("ExecStart=").Append(ServiceIdentity.ExecutablePathFor(directory)).Append(" --service\n");
            unit.Append("WorkingDirectory=").Append(folder).Append('\n');
            // The account that owns the folder, so the server can still write its settings, its
            // log and its own updates. Nothing new is created and no password exists.
            unit.Append("User=").Append(owner).Append('\n');
            // The updater is left running when the server exits, and by default systemd clears
            // out everything still in the unit's control group at that moment, which would kill
            // it partway through replacing the folder. Only the main process is ours to stop;
            // what it left behind has work to finish.
            unit.Append("KillMode=process\n");
            // The server exits on its own to apply an update and counts on being brought back.
            unit.Append("Restart=always\n");
            // Short, because what an update needs is now waited for directly above rather than
            // guessed at here. A server that cannot start at all still stops being retried:
            // systemd gives up after five attempts this close together, which is the answer
            // wanted for a server that is broken rather than busy.
            unit.Append("RestartSec=2\n\n");
            unit.Append("[Install]\n");
            unit.Append("WantedBy=multi-user.target\n");
            return unit.ToString();
        }

        /// <summary>
        /// The job description, built apart from writing it for the same reason as the systemd
        /// one: it can be checked here, and it cannot be checked on a machine we have.
        /// </summary>
        public static string BuildLaunchdPlist(string directory)
        {
            return BuildLaunchdPlist(directory, ServiceIdentity.OwningUserName());
        }

        public static string BuildLaunchdPlist(string directory, string owner)
        {
            var folder = ServiceIdentity.DisplayPath(directory);
            var label = UnitNameFor(directory);

            var plist = new StringBuilder();
            plist.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            plist.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
            plist.Append("<plist version=\"1.0\">\n<dict>\n");
            AppendKey(plist, "Label", label);
            // The same wait systemd runs before the server, which launchd has no separate step
            // for: it runs one program, so the wait goes in front of the server and hands over
            // to it. Handing over rather than starting it is what keeps the server the process
            // launchd is watching, instead of a shell that has outlived its one useful moment.
            //
            // The argument tells the server it is being managed, so the updater leaves starting
            // it again to launchd rather than launching a copy launchd knows nothing about.
            var start = "i=0; while [ -e " + Updates.UpdateMarker.FileName +
                " ] && [ $i -lt 60 ]; do sleep 1; i=$((i+1)); done; exec \"" +
                ServiceIdentity.ExecutablePathFor(directory) + "\" --service";
            plist.Append("  <key>ProgramArguments</key>\n  <array>\n    <string>/bin/sh</string>\n    <string>-c</string>\n    <string>")
                .Append(Escape(start))
                .Append("</string>\n  </array>\n");
            AppendKey(plist, "WorkingDirectory", folder);
            AppendKey(plist, "UserName", owner);
            plist.Append("  <key>RunAtLoad</key>\n  <true/>\n");
            plist.Append("  <key>KeepAlive</key>\n  <true/>\n");
            // Launchd's own ten seconds, stated rather than relied on. It used to be raised far
            // past that to cover an update being written, which the wait in front of the server
            // now covers directly and only for as long as it actually takes.
            plist.Append("  <key>ThrottleInterval</key>\n  <integer>10</integer>\n");
            plist.Append("</dict>\n</plist>\n");
            return plist.ToString();
        }

        private static void AppendKey(StringBuilder builder, string key, string value)
        {
            builder.Append("  <key>").Append(key).Append("</key>\n  <string>").Append(Escape(value)).Append("</string>\n");
        }

        private static string Escape(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
        }

        public static string UnitNameFor(string directory)
        {
            var key = ServiceIdentity.NameFor(directory).ToLowerInvariant();
            return OperatingSystem.IsMacOS()
                ? string.Format(CultureInfo.InvariantCulture, "org.{0}", key)
                : key + ".service";
        }
    }
}
