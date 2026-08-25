using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using TopSpeed.Runtime;
using SdlRuntime = TS.Sdl.Runtime;

namespace TopSpeed.Windowing.Sdl
{
    /// <summary>
    /// Runs work on the thread that owns the SDL window. macOS speech has to be driven from the
    /// main thread, and unlike a toolkit event loop SDL offers nothing to marshal onto, so the
    /// window host drains this queue on every pump.
    /// </summary>
    internal sealed class MainThreadDispatcher : ISpeechThreadDispatcher
    {
        // Long enough that a slow screen reader call still completes, short enough that the game
        // loop recovers instead of hanging for good if the window loop has stopped pumping.
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(2);
        private readonly object _sync = new object();
        private readonly Queue<Action> _pending = new Queue<Action>();
        private volatile bool _draining;

        public T Invoke<T>(Func<T> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            // Already where the work belongs, and queueing here would deadlock against ourselves.
            // This is also the path during startup, before the pump loop begins draining.
            if (!_draining || SdlRuntime.IsMainThread())
                return action();

            T result = default!;
            Exception? error = null;
            using var completed = new ManualResetEventSlim(false);

            lock (_sync)
            {
                _pending.Enqueue(() =>
                {
                    try
                    {
                        result = action();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        completed.Set();
                    }
                });
            }

            // A timeout means the window stopped pumping, which happens while shutting down.
            // Give up on the result rather than running it off the main thread.
            if (!completed.Wait(WaitTimeout))
                return default!;

            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();

            return result;
        }

        /// <summary>Runs work that has no answer to wait for, but still has to run on this thread.</summary>
        public void Invoke(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            Invoke(() =>
            {
                action();
                return true;
            });
        }

        /// <summary>Called from the window loop, on the thread that owns the window.</summary>
        public void Drain()
        {
            _draining = true;

            while (true)
            {
                Action work;
                lock (_sync)
                {
                    if (_pending.Count == 0)
                        return;

                    work = _pending.Dequeue();
                }

                work();
            }
        }

        /// <summary>Stops queueing once the loop can no longer drain, so callers run inline instead.</summary>
        public void Stop()
        {
            _draining = false;

            while (true)
            {
                Action work;
                lock (_sync)
                {
                    if (_pending.Count == 0)
                        return;

                    work = _pending.Dequeue();
                }

                work();
            }
        }
    }
}
