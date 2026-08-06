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
        /// What appears in the service manager's list. The folder and port are what tell two
        /// installations apart at a glance, which is the whole question somebody scrolling that
        /// list is trying to answer.
        /// </summary>
        public static string DisplayNameFor(string directory, int port)
        {
            return LocalizationService.Format(
                LocalizationService.Mark("TopSpeed Server ({0}, port {1})"),
                LeafName(directory),
                port.ToString(CultureInfo.InvariantCulture));
        }

        public static string DescriptionFor(string directory)
        {
            return LocalizationService.Format(
                LocalizationService.Mark("TopSpeed multiplayer race server running from {0}."),
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
