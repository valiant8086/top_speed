using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TopSpeed.Server.Logging
{
    internal sealed class Logger : IDisposable
    {
        private readonly LogLevel _enabledLevels;
        private readonly object _lock = new object();
        private readonly StreamWriter? _writer;
        private bool _writeToConsole;

        /// <summary>
        /// Set when a log file was asked for but could not be opened, so the server can say so
        /// and carry on rather than refusing to run over a log.
        /// </summary>
        public string? FileError { get; }

        /// <summary>
        /// A log configured in settings.json appends, because its whole point is to still be
        /// readable later; one asked for with --log-file starts clean for that run.
        /// </summary>
        public Logger(LogLevel enabledLevels, string? logFilePath, bool writeToConsole = true, bool append = false)
        {
            _enabledLevels = enabledLevels;
            _writeToConsole = writeToConsole;
            if (string.IsNullOrWhiteSpace(logFilePath))
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath) ?? ".");

                // Held open for as long as the server runs, and shared, so that the log of a
                // server running unattended can be read while it runs rather than only afterwards.
                //
                // Windows settles sharing in both directions, so this reaches only a reader that
                // permits a writer. Notepad and most log viewers do; an editor asking for the file
                // in the plain way is refused until the server stops. Closing the file between
                // messages would admit every reader and has been measured at about seventy times
                // the cost per message, nearly all of it spent opening the file again, so the
                // reader that cannot be served is the one left unserved.
                //
                // Sharing is limited to reading: a second writer would interleave lines into
                // nonsense, and this stays the one thing writing here.
                var stream = new FileStream(
                    logFilePath,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read | FileShare.Delete);

                _writer = new StreamWriter(stream, Encoding.UTF8)
                {
                    AutoFlush = true
                };
            }
            catch (Exception ex)
            {
                // Not being able to write a log is never a reason to refuse to run a server.
                _writer = null;
                FileError = ex.Message;
            }
        }

        public void Debug(string message) => Log(LogLevel.Debug, message);
        public void Info(string message) => Log(LogLevel.Info, message);
        public void Warning(string message) => Log(LogLevel.Warning, message);
        public void Error(string message) => Log(LogLevel.Error, message);
        public void Raw(string message) => WriteRaw(message);

        public void Log(LogLevel level, string message)
        {
            if ((_enabledLevels & level) == 0)
                return;

            Write(level, message);
        }

        private void Write(LogLevel level, string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var levelTag = level.ToString().ToLowerInvariant();
            var consoleLine = $"[{levelTag}] {message}";
            var fileTimeLine = $"[{timestamp}]";
            var fileMessageLine = $"[{levelTag}] {message}";
            lock (_lock)
            {
                if (_writeToConsole)
                    _writeToConsole = ConsoleSink.WriteLine(consoleLine);
                if (_writer != null)
                {
                    _writer.WriteLine(fileTimeLine);
                    _writer.WriteLine(fileMessageLine);
                }
            }
        }

        private void WriteRaw(string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            lock (_lock)
            {
                if (_writeToConsole)
                    _writeToConsole = ConsoleSink.WriteLine(message);
                if (_writer != null)
                {
                    _writer.WriteLine($"[{timestamp}]");
                    _writer.WriteLine(message);
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
                _writer?.Dispose();
        }
    }
}
