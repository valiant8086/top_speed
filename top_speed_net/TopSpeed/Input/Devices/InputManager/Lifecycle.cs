using System;

namespace TopSpeed.Input
{
    internal sealed partial class InputService
    {
        public void Suspend()
        {
            _suspended = true;
            _keyboardBackend.Suspend();
            _controllerBackend.Suspend();
        }

        public void Resume()
        {
            _suspended = false;
            _keyboardBackend.Resume();
            _controllerBackend.Resume();

            // Whatever ended the thing that suspended us is very likely still held: the Return that
            // sent a chat message, the Escape that closed a prompt. Without this it lands in the
            // game the moment input comes back, sending the message and then activating whatever
            // Return does behind it. Nothing counts again until it is let go of.
            _keyboardBackend.ResetHeldState();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _controllerBackend.NoControllerDetected -= OnNoControllerDetected;
            if (_gestureEventSource != null)
                _gestureEventSource.GestureRaised -= OnGestureRaised;
            if (_touchZoneGestureEventSource != null)
                _touchZoneGestureEventSource.TouchZoneGestureRaised -= OnTouchZoneGestureRaised;
            if (_touchZoneTouchEventSource != null)
                _touchZoneTouchEventSource.TouchZoneTouchRaised -= OnTouchZoneTouchRaised;
            SafeRelease(() => _controllerBackend.Dispose());
            SafeRelease(() => _keyboardBackend.Dispose());
        }

        private static void SafeRelease(Action release)
        {
            try
            {
                release();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (NullReferenceException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}

