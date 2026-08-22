using System;
using TopSpeed.Runtime;

namespace TopSpeed.Input.Devices.Keyboard.Backends.Eto
{
    internal sealed class Device : IKeyboardDevice
    {
        private readonly object _lock = new object();
        private readonly bool[] _keys;
        private readonly IKeyboardEventSource _eventSource;
        private bool _suspended;
        private bool _disposed;

        public Device(IKeyboardEventSource eventSource)
        {
            _eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
            _keys = new bool[256];
            _eventSource.KeyDown += OnKeyDown;
            _eventSource.KeyUp += OnKeyUp;
        }

        public bool TryPopulateState(InputState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            lock (_lock)
            {
                if (_disposed)
                    return false;

                ReleaseKeysNoLongerHeld();

                for (var i = 0; i < _keys.Length; i++)
                {
                    if (_keys[i])
                        state.Set((InputKey)i, true);
                }
            }

            return true;
        }

        // A key we believe is down but the hardware says is not can only have got that way by losing
        // its key-up — something else consumed the release before it reached the window. Left alone it
        // stays held forever: the throttle keeps accelerating, the car keeps steering. Checking the
        // real state each time it is read costs a fraction of a microsecond per held key and puts the
        // two views back in agreement within a frame.
        //
        // Only releases, never presses. A key held while another window had focus is not input this
        // game should act on, and inventing a press from hardware state would do exactly that.
        private void ReleaseKeysNoLongerHeld()
        {
            if (_suspended || !MacKeyState.IsAvailable)
                return;

            for (var i = 0; i < _keys.Length; i++)
            {
                if (!_keys[i])
                    continue;
                if (MacKeyState.TryIsPhysicallyDown((InputKey)i, out var held) && !held)
                    _keys[i] = false;
            }
        }

        public bool IsDown(InputKey key)
        {
            var index = (int)key;
            if (index < 0 || index >= _keys.Length)
                return false;

            lock (_lock)
            {
                if (_disposed)
                    return false;
                return _keys[index];
            }
        }

        public bool IsAnyKeyHeld(bool ignoreModifiers)
        {
            lock (_lock)
            {
                if (_disposed)
                    return false;

                for (var i = 0; i < _keys.Length; i++)
                {
                    if (!_keys[i])
                        continue;
                    if (ignoreModifiers && IsModifier((InputKey)i))
                        continue;
                    return true;
                }
            }

            return false;
        }

        public void Suspend()
        {
            lock (_lock)
            {
                _suspended = true;
                ClearKeys();
            }
        }

        public void ResetHeldState()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                ClearKeys();
            }
        }

        public void Resume()
        {
            lock (_lock)
            {
                _suspended = false;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                ClearKeys();
            }

            _eventSource.KeyDown -= OnKeyDown;
            _eventSource.KeyUp -= OnKeyUp;
        }

        private void OnKeyDown(InputKey key)
        {
            var index = (int)key;
            if (index <= 0 || index >= _keys.Length)
                return;

            lock (_lock)
            {
                if (_disposed || _suspended)
                    return;
                _keys[index] = true;
            }
        }

        private void OnKeyUp(InputKey key)
        {
            var index = (int)key;
            if (index <= 0 || index >= _keys.Length)
                return;

            lock (_lock)
            {
                if (_disposed)
                    return;
                _keys[index] = false;
            }
        }

        private void ClearKeys()
        {
            for (var i = 0; i < _keys.Length; i++)
                _keys[i] = false;
        }

        private static bool IsModifier(InputKey key)
        {
            return key == InputKey.LeftControl ||
                   key == InputKey.RightControl ||
                   key == InputKey.LeftShift ||
                   key == InputKey.RightShift ||
                   key == InputKey.LeftAlt ||
                   key == InputKey.RightAlt;
        }

    }
}
