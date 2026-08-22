using System;
using TopSpeed.Input.Devices.Controller;

namespace TopSpeed.Input
{
    internal sealed partial class DriveInput
    {
        public void Run(DriveInputFrame frame, float deltaSeconds)
        {
            var controller = frame.HasController ? frame.ControllerState : (State?)null;
            Run(frame.KeyboardState, controller, deltaSeconds, frame.ControllerIsRacingWheel);
        }

        public void Run(InputState input, float deltaSeconds)
        {
            Run(input, null, deltaSeconds, controllerIsRacingWheel: false);
        }

        public void Run(InputState input, State? controller, float deltaSeconds)
        {
            Run(input, controller, deltaSeconds, controllerIsRacingWheel: false);
        }

        public void Run(InputState input, State? controller, float deltaSeconds, bool controllerIsRacingWheel)
        {
            _prevState.CopyFrom(_lastState);
            _lastState.CopyFrom(input);
            UpdateModifierChordState();

            var wasControllerAvailable = _controllerAvailable;
            var nextWheelMode = controller.HasValue && controllerIsRacingWheel;
            var wheelModeChanged = _controllerIsRacingWheel != nextWheelMode;
            _controllerIsRacingWheel = nextWheelMode;

            if (controller.HasValue)
            {
                if (_hasPrevController)
                    _prevController = _lastController;
                _lastController = controller.Value;
                if (!_hasCenter)
                {
                    _center = controller.Value;
                    _hasCenter = true;
                }
                if (!_hasPrevController)
                    _prevController = controller.Value;
                _hasPrevController = true;
            }
            _controllerAvailable = controller.HasValue;
            if (!controller.HasValue)
            {
                _hasPrevController = false;
                _controllerIsRacingWheel = false;
            }

            if (!wasControllerAvailable || !_controllerAvailable || wheelModeChanged)
                ResetPedalCalibration();

            if (_controllerAvailable && _controllerIsRacingWheel)
                UpdatePedalCalibrationSamples();

            UpdateSimulatedInputs(deltaSeconds);
            _intentState = CaptureIntentState();
        }

        public void SetCenter(State center)
        {
            _center = center;
            _hasCenter = true;
            _settings.ControllerCenter = center;
        }

        public void SetDevice(bool useController)
        {
            SetDevice(useController ? InputDeviceMode.Controller : InputDeviceMode.Keyboard);
        }

        public void SetDevice(InputDeviceMode mode)
        {
            _deviceMode = mode;
            _settings.DeviceMode = mode;
        }
    }
}


