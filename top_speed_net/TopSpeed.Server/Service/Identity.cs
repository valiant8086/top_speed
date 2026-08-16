using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using TopSpeed.Localization;
using TopSpeed.Server.Control;

namespace TopSpeed.Server.Service
{
    /// <summary>
    /// What one install folder's service is called and where it may live.
    ///
    /// The name is derived from the folder rather than chosen or stored. Two servers in two
    /// folders therefore get two services without anybody naming them, and a service that is
    /// already running can work out what it was registered as purely from where it is running,
    /// which is what keeps the registered name and the name the program answers to from ever
    /// disagreeing. A mismatch there is what the service manager reports as the unhelpful
    /// "did not respond in a timely fashion".
    /// </summary>
    internal static class ServiceIdentity
    {
        /// <summary>
        /// The registration key. Ugly on purpose: it has to be unique per folder and legal as
        /// a service name, and the readable label is a separate thing.
        /// </summary>
        public static string NameFor(string directory)
        {
            return ControlEndpoint.InstanceKeyFor(directory);
        }

        /// <summary>
        /// The account a service installed from here should run as.
        ///
        /// Normally whoever is running this, and under sudo emphatically not: there the process
        /// is root, and a registration naming root would give the server more than it needs and
        /// leave root owned files in a folder its owner has to be able to replace. Sudo says who
        /// asked, which is the account that owns the folder and the one meant all along.
        /// </summary>
        public static string OwningUserName(string directory)
        {
            return ChooseServiceAccount(
                FolderOwner(directory),
                Environment.GetEnvironmentVariable("SUDO_USER"),
                Environment.UserName);
        }

        /// <summary>
        /// Which of the accounts on offer the service should run as.
        ///
        /// The folder's owner is asked first because it is the fact the rest are guesses at. What
        /// this decides is who has to be able to replace these files when the server updates
        /// itself, and that is settled by who owns them, not by who happened to type the command.
        /// It is also the only answer available to somebody who reached root with su, which
        /// records nothing: Debian offers a root password during installation and leaves that
        /// account out of sudo entirely, so su is the ordinary way to be root on machines set up
        /// that way, and every instruction to use sudo is useless there.
        ///
        /// Sudo comes next, for the folder that is owned by root because it was unpacked with
        /// sudo. Running as the person who asked is both safer than root and what was meant.
        ///
        /// Root itself is a real answer and not a failure. Where root owns the folder and nobody
        /// else was involved, root is the account there is, which is how a rented server or a
        /// container usually arrives.
        ///
        /// Separated from the asking so it can be checked. The three inputs are awkward to
        /// arrange on a real machine and trivial to write down.
        /// </summary>
        public static string ChooseServiceAccount(string? folderOwner, string? sudoUser, string currentUser)
        {
            var owner = (folderOwner ?? string.Empty).Trim();
            var invoker = (sudoUser ?? string.Empty).Trim();

            if (owner.Length > 0 && !IsRoot(owner))
                return owner;

            if (invoker.Length > 0 && !IsRoot(invoker))
                return invoker;

            if (owner.Length > 0)
                return owner;

            return currentUser;
        }

        /// <summary>
        /// The account a folder belongs to, as the system reports it, or null where it cannot be
        /// asked.
        ///
        /// Asked of stat rather than read through the runtime, which exposes a file's permissions
        /// and not its owner, and rather than through a p/invoke, which would mean laying out
        /// struct stat correctly for seven shipping targets whose layouts differ and only one of
        /// which can be tried here. A wrong layout does not fail; it reads whatever is next in
        /// memory and returns a plausible name. Running the program that already knows costs one
        /// process and cannot be subtly wrong.
        /// </summary>
        public static string? FolderOwner(string directory)
        {
            if (OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(directory))
                return null;

            // Same question, two spellings: BSD stat on macOS, GNU or busybox stat everywhere
            // else. Both print the name, so nothing here has to map a number to an account.
            var arguments = OperatingSystem.IsMacOS()
                ? new[] { "-f", "%Su", directory }
                : new[] { "-c", "%U", directory };

            var answer = RunForOutput("stat", arguments);
            if (answer == null)
                return null;

            answer = answer.Trim();

            // An owner with no matching account is reported as the bare number, which names
            // nobody and would be written into a unit file that then cannot start.
            if (answer.Length == 0 || answer.All(char.IsDigit))
                return null;

            return answer;
        }

        /// <summary>
        /// Whether running as root here would leave the folder's owner unable to replace what
        /// root writes into it, which is the whole of the harm and worth measuring rather than
        /// inferring.
        ///
        /// Where the owner cannot be asked this falls back to what sudo recorded, which is the
        /// same question answered less well.
        /// </summary>
        public static bool RootWouldStrandTheFolderOwner(string directory)
        {
            var owner = FolderOwner(directory);
            if (string.IsNullOrEmpty(owner))
                return RootReachedFromAnotherAccount();

            return !IsRoot(owner);
        }

        private static bool IsRoot(string account)
        {
            return string.Equals(account, "root", StringComparison.Ordinal);
        }

        private static string? RunForOutput(string program, string[] arguments)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = program,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                foreach (var argument in arguments)
                    info.ArgumentList.Add(argument);

                using var process = System.Diagnostics.Process.Start(info);
                if (process == null)
                    return null;

                var text = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return process.ExitCode == 0 ? text : null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                || ex is InvalidOperationException
                || ex is IOException)
            {
                // No stat, or nothing able to run it. The caller has a poorer answer to fall
                // back on and this is not worth failing an install over.
                return null;
            }
        }

        /// <summary>
        /// Whether root was reached from some other account, rather than being the account there
        /// is.
        ///
        /// The difference decides whether running as root does any harm. A folder owned by a
        /// person, with files in it written by root, is the thing that breaks: its owner can no
        /// longer replace what root left. Where root is the only account there is no second owner
        /// to be locked out, and nothing goes wrong at all.
        ///
        /// That case is not unusual. A rented server often arrives with root as the only login
        /// and no ordinary account is ever made, and a container is root by default. Refusing
        /// those would block a setup that works to prevent a problem it cannot have.
        ///
        /// Sudo is what tells the two apart, since it records who asked. Sudo used by root
        /// records root, which is still the one account and so still no harm. Nothing records su,
        /// which is why that route is not caught: after it, this process is indistinguishable
        /// from having logged in as root.
        /// </summary>
        public static bool RootReachedFromAnotherAccount()
        {
            var invoker = Environment.GetEnvironmentVariable("SUDO_USER");

            return !string.IsNullOrWhiteSpace(invoker)
                && !string.Equals(invoker.Trim(), "root", StringComparison.Ordinal);
        }

        /// <summary>
        /// What appears in the service manager's list.
        ///
        /// The folder alone, because that is the one thing about an installation which cannot
        /// change while it is installed. A registration is written once and nothing revisits it,
        /// so anything here that can be reconfigured later would eventually be a confident lie.
        /// The port in particular is a setting, changeable from the options menu or by editing
        /// the file, and it is shown in the service menu instead, read fresh each time.
        /// </summary>
        public static string DisplayNameFor(string directory)
        {
            return LocalizationService.Format(
                LocalizationService.Mark("TopSpeed Server ({0})"),
                LeafName(directory));
        }

        public static string DescriptionFor(string directory)
        {
            return LocalizationService.Format(
                LocalizationService.Mark("TopSpeed multiplayer race server located at {0}."),
                DisplayPath(directory));
        }

        /// <summary>The executable to register, quoted, with the argument that runs it headless.</summary>
        public static string CommandLineFor(string directory)
        {
            return "\"" + ExecutablePathFor(directory) + "\" --service";
        }

        public static string ExecutablePathFor(string directory)
        {
            return Path.Combine(DisplayPath(directory), ExecutableName());
        }

        /// <summary>
        /// The folder as it is actually spelled, for anything a person will read or that has to
        /// address the file system.
        ///
        /// The normalised form exists to make one folder hash to one name, and it folds case on
        /// Windows to do that. That is right for deciding whether two paths are the same folder
        /// and wrong for everything else: it would put a lowercased path in front of whoever
        /// reads the service list, and on a system where names are case sensitive it would not
        /// even point at the right file.
        /// </summary>
        public static string DisplayPath(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return string.Empty;

            var full = Path.GetFullPath(directory.Trim());
            var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length == 0 ? full : trimmed;
        }

        private static string ExecutableName()
        {
            return OperatingSystem.IsWindows() ? "TopSpeed.Server.exe" : "TopSpeed.Server";
        }

        /// <summary>
        /// The configured port, purely for labelling the service in the system's list.
        ///
        /// Read straight from the file rather than through the settings store, because the
        /// store writes a fresh settings file when it finds none, and doing that while elevated
        /// would leave the server a file its own account may not be able to rewrite later. A
        /// folder with no settings yet is not worth failing over, so it reports nothing.
        /// </summary>
        public static int ReadConfiguredPort(string directory)
        {
            try
            {
                var path = Path.Combine(DisplayPath(directory), "settings.json");
                if (!File.Exists(path))
                    return 0;

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "port", StringComparison.OrdinalIgnoreCase)
                        && property.Value.TryGetInt32(out var port))
                    {
                        return port;
                    }
                }

                return 0;
            }
            catch (JsonException)
            {
                return 0;
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }

        private static string LeafName(string directory)
        {
            var path = DisplayPath(directory);
            var leaf = Path.GetFileName(path);
            return string.IsNullOrEmpty(leaf) ? path : leaf;
        }

        /// <summary>
        /// Whether this folder is somewhere a server must not be installed from.
        ///
        /// The server updates itself in place, so its folder has to be writable by the account
        /// the service runs as. Granting that inside a location the system trusts turns the
        /// trust into a way up: anything that can write there gets its code run by a more
        /// privileged account later. Portable installs belong somewhere ordinary, and refusing
        /// here is cheaper than explaining the consequences afterwards.
        /// </summary>
        public static bool IsProtectedLocation(string directory, out string? location)
        {
            location = null;
            var normalized = ControlEndpoint.NormalizeDirectory(directory);
            if (normalized.Length == 0)
                return false;

            foreach (var folder in new[]
            {
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System,
                Environment.SpecialFolder.SystemX86
            })
            {
                string candidate;
                try
                {
                    candidate = Environment.GetFolderPath(folder);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(candidate))
                    continue;

                if (IsWithin(normalized, ControlEndpoint.NormalizeDirectory(candidate)))
                {
                    location = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsWithin(string normalizedChild, string normalizedParent)
        {
            if (normalizedParent.Length == 0)
                return false;

            if (string.Equals(normalizedChild, normalizedParent, StringComparison.Ordinal))
                return true;

            // The separator matters: "C:\program files extra" is not inside "C:\program files".
            var prefix = normalizedParent.EndsWith(Path.DirectorySeparatorChar)
                ? normalizedParent
                : normalizedParent + Path.DirectorySeparatorChar;

            return normalizedChild.StartsWith(prefix, StringComparison.Ordinal);
        }
    }
}
