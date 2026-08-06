using System;
using System.IO;
using System.Text;
using TopSpeed.Server.Commands;

namespace TopSpeed.Server.Control
{
    /// <summary>
    /// A command session carried over the control connection. Line based UTF-8 in both
    /// directions: whatever the server would have printed is sent, and whatever the client
    /// types comes back as a command, so every existing command and menu works unchanged.
    /// </summary>
    internal sealed class ControlCommandSession : ICommandSession, IDisposable
    {
        private readonly Stream _stream;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly object _writeGate = new object();
        private volatile bool _closed;

        public ControlCommandSession(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            _reader = new StreamReader(_stream, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            _writer = new StreamWriter(_stream, encoding, bufferSize: 4096, leaveOpen: true)
            {
                AutoFlush = true
            };
        }

        public bool CanRead => !_closed;

        public bool WriteLine(string text)
        {
            if (_closed)
                return false;

            try
            {
                // Output reaches this from several threads at once: the command being run, the
                // update scheduler, and the logger. A StreamWriter is not safe to share, and
                // interleaved writes corrupt the stream and break the connection.
                lock (_writeGate)
                    _writer.WriteLine(text ?? string.Empty);

                return true;
            }
            catch (IOException)
            {
                _closed = true;
                return false;
            }
            catch (ObjectDisposedException)
            {
                _closed = true;
                return false;
            }
        }

        public bool TryReadLine(out string value)
        {
            value = string.Empty;
            if (_closed)
                return false;

            try
            {
                // A client that is killed rather than closed cleanly still lands here: the
                // operating system tears its handle down and this read returns end of stream
                // straight away. That is why no heartbeat is needed on a local connection.
                var line = _reader.ReadLine();
                if (line == null)
                {
                    _closed = true;
                    return false;
                }

                value = line;
                return true;
            }
            catch (IOException)
            {
                _closed = true;
                return false;
            }
            catch (ObjectDisposedException)
            {
                _closed = true;
                return false;
            }
        }

        public void Dispose()
        {
            _closed = true;
            try
            {
                // The stream itself belongs to the listener, which reuses it for the next
                // client, so only the readers and writers built on top of it are torn down.
                _writer.Dispose();
                _reader.Dispose();
            }
            catch
            {
                // Tearing down a connection that is already gone is not worth reporting.
            }
        }
    }
}
