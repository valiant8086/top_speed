using TopSpeed.Input;
using TopSpeed.Localization;
using TopSpeed.Shortcuts;

namespace TopSpeed.Game
{
    internal sealed partial class Game
    {
        internal const string SwitchVehiclePanelShortcutActionId = "drive_switch_vehicle_panel";

        // Panel switching used to be an if in Query.cs - WasPressed(Tab) && IsCtrlDown() - which put it
        // outside the binding system entirely: not remappable, not discoverable, and invisible to the
        // conflict checks every other binding goes through. Registering it makes Control+Tab a default
        // rather than a constant, and lets DriveInput see that Tab is spoken for when Control is held.
        //
        // The trigger is deliberately empty. Dispatch belongs to the drive session, which is the only
        // place that knows there are panels to switch between; the drive layer edge-detects this
        // binding itself through GetPanelSwitchRequest. Registering it here is what owns the binding.
        private void RegisterDriveShortcutActions()
        {
            _menu.RegisterShortcutAction(
                SwitchVehiclePanelShortcutActionId,
                LocalizationService.Mark("Switch vehicle panel"),
                LocalizationService.Mark("Moves to the next panel while driving."),
                InputKey.Tab,
                new ShortcutModifiers(shift: false, control: true, alt: false),
                () => { });
        }

        private bool WasPanelSwitchShortcutPressed()
        {
            return _menu.WasShortcutActionTriggered(SwitchVehiclePanelShortcutActionId, _input);
        }
    }
}
