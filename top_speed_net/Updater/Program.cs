using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;

namespace TopSpeed.Updater
{
    internal static class Program
    {
        private const int ExtractRetryCount = 24;
        private const int ExtractRetryDelayMs = 250;

        private static int Main(string[] args)
        {
            var safeArgs = args ?? Array.Empty<string>();
            var enableLog = HasLogFlag(safeArgs);
            var logPath = enableLog ? ResolveLogPath(safeArgs) : string.Empty;
            Log(enableLog, logPath, "Updater entry.");
            Log(enableLog, logPath, "Args: " + string.Join(" ", safeArgs));
            try
            {
                var options = ParseArgs(safeArgs);
                enableLog = options.EnableLog;
                logPath = enableLog ? Path.Combine(Path.GetFullPath(options.TargetDir), "updater.log") : string.Empty;
                Log(enableLog, logPath, $"Parsed args. pid={options.ProcessId}, zip={options.ZipPath}, dir={options.TargetDir}, game={options.GameExeName}, skip={options.SkipFileName}, noRestart={options.NoRestart}, startService={options.StartService}");
                WaitForProcessExit(options.ProcessId);
                Log(enableLog, logPath, "Waited for game process exit.");
                InstallZip(options, enableLog, logPath);
                Log(enableLog, logPath, "Zip install complete.");
                ClearUpdateMarker(options.TargetDir, enableLog, logPath);

                // Checked before --no-restart, which is also passed, so that an older copy of
                // this program still does the safe thing with a flag it does not know.
                if (options.StartService)
                {
                    StartService(options, enableLog, logPath);
                    return 0;
                }

                if (options.NoRestart)
                {
                    Log(enableLog, logPath, "Restart left to the service manager.");
                    return 0;
                }

                StartGame(options, enableLog, logPath);
                Log(enableLog, logPath, "Game restart requested successfully.");
                return 0;
            }
            catch (Exception ex)
            {
                Log(enableLog, logPath, "Updater failed: " + ex);
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static UpdaterOptions ParseArgs(string[] args)
        {
            var options = new UpdaterOptions();
            for (var i = 0; i < args.Length; i++)
            {
                var key = args[i] ?? string.Empty;
                if (string.Equals(key, "--log", StringComparison.OrdinalIgnoreCase))
                {
                    options.EnableLog = true;
                    continue;
                }

                // Files are replaced and that is all. Used when something else owns starting the
                // program again, which is the case for a server running as a system service:
                // launching the executable directly would produce a running program that the
                // service manager knows nothing about, holding the folder its own service needs.
                if (string.Equals(key, "--no-restart", StringComparison.OrdinalIgnoreCase))
                {
                    options.NoRestart = true;
                    continue;
                }

                // Passed alongside --no-restart rather than instead of it. This program is never
                // replaced by an update, since it is the one holding the files open, so the copy
                // that runs during an update is whichever one was first installed. An older copy
                // ignores a flag it has never heard of and falls back to --no-restart, which
                // leaves the service manager to notice, exactly as it did before.
                if (string.Equals(key, "--start-service", StringComparison.OrdinalIgnoreCase))
                {
                    options.StartService = true;
                    continue;
                }

                var value = i + 1 < args.Length ? (args[i + 1] ?? string.Empty) : string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                switch (key)
                {
                    case "--pid":
                        if (int.TryParse(value, out var pid))
                        {
                            options.ProcessId = pid;
                            i++;
                        }
                        break;
                    case "--zip":
                        options.ZipPath = value;
                        i++;
                        break;
                    case "--dir":
                        options.TargetDir = value;
                        i++;
                        break;
                    case "--game":
                        options.GameExeName = value;
                        i++;
                        break;
                    case "--skip":
                        options.SkipFileName = value;
                        i++;
                        break;
                }
            }

            if (options.ProcessId <= 0)
                throw new InvalidOperationException("Missing or invalid --pid argument.");
            if (string.IsNullOrWhiteSpace(options.ZipPath))
                throw new InvalidOperationException("Missing --zip argument.");
            if (string.IsNullOrWhiteSpace(options.TargetDir))
                throw new InvalidOperationException("Missing --dir argument.");
            if (string.IsNullOrWhiteSpace(options.GameExeName))
                throw new InvalidOperationException("Missing --game argument.");

            return options;
        }

        private static void WaitForProcessExit(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                process.WaitForExit();
                Thread.Sleep(ExtractRetryDelayMs);
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
        }

        private static void InstallZip(UpdaterOptions options, bool enableLog, string logPath)
        {
            var zipPath = Path.GetFullPath(options.ZipPath);
            var targetDir = Path.GetFullPath(options.TargetDir);
            Log(enableLog, logPath, $"InstallZip start. zip={zipPath}");
            if (!File.Exists(zipPath))
                throw new FileNotFoundException("Update zip was not found.", zipPath);
            if (!Directory.Exists(targetDir))
                throw new DirectoryNotFoundException($"Target directory was not found: {targetDir}");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var bundlePayloadPrefix = ResolveBundlePayloadPrefix(options, archive, targetDir);
                Log(enableLog, logPath, $"Archive opened. entries={archive.Entries.Count}, bundlePrefix={bundlePayloadPrefix}");
                var extractedCount = 0;
                for (var i = 0; i < archive.Entries.Count; i++)
                {
                    var entry = archive.Entries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.FullName))
                        continue;
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    var relativePath = ResolveRelativeEntryPath(entry.FullName, bundlePayloadPrefix);
                    if (string.IsNullOrWhiteSpace(relativePath))
                        continue;

                    if (ShouldSkipEntry(options.SkipFileName, entry.Name))
                    {
                        Log(enableLog, logPath, $"Skipped entry: {entry.FullName}");
                        continue;
                    }

                    var destination = Path.GetFullPath(Path.Combine(targetDir, relativePath));
                    if (!destination.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Unsafe entry path: {entry.FullName}");

                    var parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrWhiteSpace(parent))
                        Directory.CreateDirectory(parent);

                    ExtractEntryWithRetry(entry, destination, enableLog, logPath);
                    extractedCount++;
                }

                Log(enableLog, logPath, $"Archive extraction finished. extracted={extractedCount}");
            }

            File.Delete(zipPath);
            Log(enableLog, logPath, "Deleted update zip.");
        }

        private static string ResolveBundlePayloadPrefix(UpdaterOptions options, ZipArchive archive, string targetDir)
        {
            if (archive == null || string.IsNullOrWhiteSpace(targetDir))
                return string.Empty;

            var normalizedTargetDir = NormalizeZipStylePath(targetDir).TrimEnd('/');
            if (!normalizedTargetDir.EndsWith("/Contents/MacOS", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            var bundlePrefix = $"{options.GameExeName}.app/Contents/MacOS/";
            for (var i = 0; i < archive.Entries.Count; i++)
            {
                var entry = archive.Entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.FullName))
                    continue;

                var normalizedEntryPath = NormalizeZipStylePath(entry.FullName);
                if (normalizedEntryPath.StartsWith(bundlePrefix, StringComparison.OrdinalIgnoreCase))
                    return bundlePrefix;
            }

            return string.Empty;
        }

        private static string ResolveRelativeEntryPath(string entryFullName, string bundlePayloadPrefix)
        {
            var normalizedEntryPath = NormalizeZipStylePath(entryFullName);
            if (string.IsNullOrWhiteSpace(normalizedEntryPath))
                return string.Empty;

            if (!string.IsNullOrEmpty(bundlePayloadPrefix))
            {
                if (!normalizedEntryPath.StartsWith(bundlePayloadPrefix, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                normalizedEntryPath = normalizedEntryPath.Substring(bundlePayloadPrefix.Length);
                if (string.IsNullOrWhiteSpace(normalizedEntryPath))
                    return string.Empty;
            }

            return normalizedEntryPath.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string NormalizeZipStylePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        /// <summary>
        /// Puts the service back now that the files are in place.
        ///
        /// Done by running the server with the flag that starts this folder's service, rather
        /// than by talking to the service manager here. That flag already knows how to find the
        /// service belonging to a folder and how to report what happened, and the alternative is
        /// a second copy of all of it in a program whose whole job is unpacking a zip.
        ///
        /// The program started is the one just written, which is the point: it is the new
        /// version that registers as running.
        /// </summary>
        /// <summary>
        /// Removes the file the server raised before it exited, which is what the systemd and
        /// launchd units wait on before starting the new server. The folder is whole by the time
        /// this runs, so the wait has nothing left to wait for.
        ///
        /// Failing to remove it is not worth failing an install over: the units give up on it
        /// after a minute regardless, and the server clears it at startup as well.
        /// </summary>
        private static void ClearUpdateMarker(string targetDir, bool enableLog, string logPath)
        {
            try
            {
                var marker = Path.Combine(Path.GetFullPath(targetDir), ".updating");
                if (File.Exists(marker))
                {
                    File.Delete(marker);
                    Log(enableLog, logPath, "Cleared update marker.");
                }
            }
            catch (Exception ex)
            {
                Log(enableLog, logPath, "Could not clear update marker: " + ex.Message);
            }
        }

        private static void StartService(UpdaterOptions options, bool enableLog, string logPath)
        {
            var serverPath = ResolveGamePath(options.TargetDir, options.GameExeName);
            Log(enableLog, logPath, $"Resolved server path for service start: {serverPath}");
            if (string.IsNullOrWhiteSpace(serverPath) || !File.Exists(serverPath))
            {
                throw new FileNotFoundException(
                    "Updated server executable was not found.",
                    Path.Combine(options.TargetDir, ResolveExecutableFileName(options.GameExeName)));
            }

            var workingDirectory = Path.GetDirectoryName(serverPath);
            if (string.IsNullOrWhiteSpace(workingDirectory))
                workingDirectory = options.TargetDir;

            var startInfo = new ProcessStartInfo
            {
                FileName = serverPath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                // One argument with nothing in it needing quotes. The list form this would
                // otherwise use does not exist on the older framework this program also builds
                // for, and a single flag needs none of what it offers.
                Arguments = "--start-service"
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Log(enableLog, logPath, "Process.Start returned null for the service start.");
                return;
            }

            // Waited for so the outcome can be recorded. Nothing depends on it succeeding: a
            // start that fails leaves the service stopped, which is what the manager's own
            // restart is still there to catch.
            process.WaitForExit();
            Log(enableLog, logPath, $"Service start finished with exit code {process.ExitCode}.");
        }

        private static void StartGame(UpdaterOptions options, bool enableLog, string logPath)
        {
            var gamePath = ResolveGamePath(options.TargetDir, options.GameExeName);
            Log(enableLog, logPath, $"Resolved game path: {gamePath}");
            if (string.IsNullOrWhiteSpace(gamePath) || !File.Exists(gamePath))
                throw new FileNotFoundException(
                    "Updated game executable was not found.",
                    Path.Combine(options.TargetDir, ResolveExecutableFileName(options.GameExeName)));

            var workingDirectory = Path.GetDirectoryName(gamePath);
            if (string.IsNullOrWhiteSpace(workingDirectory))
                workingDirectory = options.TargetDir;

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = gamePath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            });
            Log(enableLog, logPath, process == null
                ? "Process.Start returned null for game restart."
                : $"Game restart process started. pid={process.Id}");
        }

        private static void ExtractEntryWithRetry(ZipArchiveEntry entry, string destination, bool enableLog, string logPath)
        {
            Exception? lastError = null;
            for (var attempt = 1; attempt <= ExtractRetryCount; attempt++)
            {
                try
                {
                    entry.ExtractToFile(destination, overwrite: true);
                    if (attempt > 1)
                        Log(enableLog, logPath, $"Extract retry succeeded for {destination} on attempt {attempt}.");
                    return;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                    Log(enableLog, logPath, $"Extract retry {attempt}/{ExtractRetryCount} failed for {destination}: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                    Log(enableLog, logPath, $"Extract retry {attempt}/{ExtractRetryCount} failed for {destination}: {ex.Message}");
                }

                Thread.Sleep(ExtractRetryDelayMs);
            }

            throw new IOException(
                $"Failed to extract '{entry.FullName}' to '{destination}' after {ExtractRetryCount} attempts.",
                lastError);
        }

        private static string ResolveGamePath(string targetDir, string gameExeName)
        {
            var resolvedFileName = ResolveExecutableFileName(gameExeName);
            var directPath = Path.Combine(targetDir, resolvedFileName);
            if (File.Exists(directPath))
                return directPath;

            var matches = Directory.GetFiles(targetDir, resolvedFileName, SearchOption.AllDirectories);
            if (matches.Length == 0)
                return directPath;
            if (matches.Length == 1)
                return matches[0];

            var bestMatch = matches[0];
            var bestDepth = GetPathDepth(bestMatch);
            for (var i = 1; i < matches.Length; i++)
            {
                var candidate = matches[i];
                var candidateDepth = GetPathDepth(candidate);
                if (candidateDepth < bestDepth)
                {
                    bestMatch = candidate;
                    bestDepth = candidateDepth;
                }
            }

            return bestMatch;
        }

        private static bool ShouldSkipEntry(string skipStem, string entryName)
        {
            if (string.IsNullOrWhiteSpace(skipStem) || string.IsNullOrWhiteSpace(entryName))
                return false;

            var runtimeFileName = ResolveExecutableFileName(skipStem);
            if (string.Equals(entryName, runtimeFileName, StringComparison.OrdinalIgnoreCase))
                return true;

            var entryStem = Path.GetFileNameWithoutExtension(entryName);
            return string.Equals(entryStem, skipStem, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetPathDepth(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return int.MaxValue;

            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath) ?? string.Empty;
            var relative = fullPath.Substring(root.Length);
            if (relative.Length == 0)
                return 0;

            var segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length;
        }

        private static string ResolveExecutableFileName(string stem)
        {
            if (string.IsNullOrWhiteSpace(stem))
                throw new ArgumentException("Executable stem is required.", nameof(stem));

            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? stem + ".exe"
                : stem;
        }

        private static bool HasLogFlag(string[] args)
        {
            if (args == null || args.Length == 0)
                return false;

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--log", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string ResolveLogPath(string[] args)
        {
            try
            {
                for (var i = 0; i < args.Length - 1; i++)
                {
                    if (!string.Equals(args[i], "--dir", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var dir = args[i + 1];
                    if (string.IsNullOrWhiteSpace(dir))
                        break;

                    return Path.Combine(Path.GetFullPath(dir), "updater.log");
                }
            }
            catch
            {
            }

            return Path.Combine(Path.GetTempPath(), "topspeed_updater.log");
        }

        private static void Log(bool enabled, string path, string message)
        {
            if (!enabled)
                return;

            WriteLog(path, message);
        }

        private static void WriteLog(string path, string message)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(path, line);
            }
            catch
            {
                // Ignore logging failures.
            }
        }


        private sealed class UpdaterOptions
        {
            public int ProcessId { get; set; }
            public string ZipPath { get; set; } = string.Empty;
            public string TargetDir { get; set; } = string.Empty;
            public string GameExeName { get; set; } = string.Empty;
            public string SkipFileName { get; set; } = string.Empty;
            public bool EnableLog { get; set; }
            public bool NoRestart { get; set; }
            public bool StartService { get; set; }
        }
    }
}
