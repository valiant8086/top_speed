using System;
using TopSpeed.Drive.Panels;
using TopSpeed.Input;

namespace TopSpeed.Drive.Session.Systems
{
    internal sealed class Panels : Subsystem
    {
        private readonly DriveInput _input;
        private readonly VehiclePanelManager _panels;
        private readonly RadioVehiclePanel _radioPanel;
        private readonly Action<string> _speakText;

        public Panels(
            string name,
            int order,
            DriveInput input,
            VehiclePanelManager panels,
            RadioVehiclePanel radioPanel,
            Action<string> speakText)
            : base(name, order)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _panels = panels ?? throw new ArgumentNullException(nameof(panels));
            _radioPanel = radioPanel ?? throw new ArgumentNullException(nameof(radioPanel));
            _speakText = speakText ?? throw new ArgumentNullException(nameof(speakText));
        }

        public override void Update(SessionContext context, float elapsed)
        {
            if (!ReferenceEquals(_panels.ActivePanel, _radioPanel))
                _radioPanel.Tick(elapsed);

            // One cycling request rather than a forward and a backward one: there are two panels, so
            // MovePrevious lands where MoveNext does. MovePrevious stays on the manager for when a
            // third panel makes a backward binding worth having.
            var panelChanged = false;
            if (_input.GetPanelSwitchRequest())
            {
                _panels.MoveNext();
                panelChanged = true;
            }

            if (panelChanged)
            {
                RestoreActiveInputAccess();
                _speakText(SessionText.FormatPanelAnnouncement(_panels.ActivePanel.Name));
            }

            _panels.Update(elapsed);
        }

        public void Pause()
        {
            _panels.Pause();
        }

        public void Resume()
        {
            _panels.Resume();
        }

        public void ApplyInputPolicy(InputPolicy policy)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));

            _input.SetPanelInputAccess(policy.AllowDrivingInput, policy.AllowAuxiliaryInput);
            _input.SetPausedHornInputAllowed(policy.AllowHorn);
        }

        public void RestoreActiveInputAccess()
        {
            var panel = _panels.ActivePanel;
            _input.SetPanelInputAccess(panel.AllowsDrivingInput, panel.AllowsAuxiliaryInput);
        }
    }
}

