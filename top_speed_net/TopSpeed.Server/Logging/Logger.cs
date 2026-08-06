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
        /// A log configured in settings.json appends, because its whole point is to still be
        /// readable later; one asked for with --log-file starts clean for that run.
        /// </summary>
        public Logger(LogLevel enabledLevels, string? logFilePath, bool writeToConsole = true, bool append = false)
        {
            _enabledLevels = enabledLevels;
            _writeToConsole = writeToConsole;
            if (!string.IsNullOrWhiteSpace(logFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath) ?? ".");
                _writer = new StreamWriter(logFilePath, append, Encoding.UTF8)
                {
                    AutoFlush = true
                };
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
