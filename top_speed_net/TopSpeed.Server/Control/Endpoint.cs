using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TopSpeed.Server.Control
{
    /// <summary>
    /// Works out where an instance running from a given folder can be reached.
    ///
    /// The address has to be a function of the install directory so that two copies started
    /// from the same folder find each other and copies in different folders never do. Each
    /// platform gets the primitive that suits it: Windows named pipes live in one flat global
    /// namespace and so carry a hash of the path, while a unix socket is simply a file placed
    /// in the folder itself, where "same location" needs no encoding at all.
    /// </summary>
    internal static class ControlEndpoint
    {
        public const string SocketFileName = "server.sock";
        public const string StatusFileName = "server.status";
        private const string PipePrefix = "TopSpeedServer-";

        public static bool UsesNamedPipe => OperatingSystem.IsWindows();

        /// <summary>
        /// Case and separators are normalised so that C:\Servers\A and c:\servers\a\ agree, and
        /// so that the same folder always produces the same name across restarts.
        /// </summary>
        public static string NormalizeDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return string.Empty;

            var full = Path.GetFullPath(directory.Trim());
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Length == 0)
                full = Path.GetFullPath(directory.Trim());

            // Windows paths are case insensitive; unix paths are not, and folding them would
            // wrongly merge two genuinely different directories.
            return OperatingSystem.IsWindows()
                ? full.ToLowerInvariant()
                : full;
        }

        /// <summary>
        /// A name unique to one install folder, in a form that is legal everywhere a name is
        /// needed. Both the control endpoint and the service registration use this, so a
        /// running service can work out what it was registered as by looking at the folder it
        /// is running from, and the two can never drift apart or need storing anywhere.
        /// </summary>
        public static string InstanceKeyFor(string directory)
        {
            var normalized = NormalizeDirectory(directory);
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            var builder = new StringBuilder(PipePrefix.Length + 16);
            builder.Append(PipePrefix);
            for (var i = 0; i < 8; i++)
                builder.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));

            return builder.ToString();
        }

        public static string PipeNameFor(string directory)
        {
            return InstanceKeyFor(directory);
        }

        public static string SocketPathFor(string directory)
        {
            return Path.Combine(NormalizeDirectory(directory), SocketFileName);
        }

        public static string StatusPathFor(string directory)
        {
            return Path.Combine(NormalizeDirectory(directory), StatusFileName);
        }

        /// <summary>The address to hand to a pipe or socket API for this directory.</summary>
        public static string AddressFor(string directory)
        {
            return UsesNamedPipe ? PipeNameFor(directory) : SocketPathFor(directory);
        }
    }
}
