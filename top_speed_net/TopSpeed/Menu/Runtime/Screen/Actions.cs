using TopSpeed.Speech;

namespace TopSpeed.Menu
{
    internal sealed partial class MenuScreen
    {
        private bool TryHandleItemAdjustment(UpdateInputState state)
        {
            if (_index == NoSelection)
                return false;

            var adjustment = GetAdjustmentAction(
                state.MoveLeft,
                state.MoveRight,
                state.PageUp,
                state.PageDown,
                state.MoveHome,
                state.MoveEnd);
            if (!adjustment.HasValue)
                return false;

            var item = _items[_index];
            if (!item.Adjust(adjustment.Value, out var announcement))
                return false;

            PlayNavigateSound();
            var safeAnnouncement = announcement;
            if (!string.IsNullOrWhiteSpace(safeAnnouncement))
            {
                _speech.Speak(safeAnnouncement!, SpeechService.SpeakFlag.NoInterruptButStop);
                CancelHint();
            }

            return true;
        }

        private bool TryHandleActionBrowse(UpdateInputState state)
        {
            if (_index == NoSelection)
                return false;

            var currentItem = _items[_index];
            if (!currentItem.HasActions)
            {
                _activeActionIndex = NoSelection;
                return false;
            }

            if (state.MoveRight && TryBrowseItemActions(currentItem, +1))
                return true;

            if (state.MoveLeft && _activeActionIndex != NoSelection && TryBrowseItemActions(currentItem, -1))
                return true;

            return false;
        }

        private void HandleMusicAdjustment(UpdateInputState state)
        {
            if (state.PageUp)
            {
                SetMusicVolume(_musicVolume + 0.05f);
            }
            else if (state.PageDown)
            {
                SetMusicVolume(_musicVolume - 0.05f);
            }
        }

        private MenuUpdateResult HandleActivation()
        {
            if (_index == NoSelection)
                return MenuUpdateResult.None;

            if (_activeActionIndex != NoSelection)
            {
                var item = _items[_index];
                if (item.TryActivateAction(_activeActionIndex))
                {
                    PlaySfx(_activateSound);
                    CancelHint();
                    return MenuUpdateResult.None;
                }
            }

            PlaySfx(_activateSound);
            CancelHint();
            return MenuUpdateResult.Activated(_items[_index]);
        }

        private bool TryBrowseItemActions(MenuItem item, int direction)
        {
            if (!item.HasActions || item.ActionCount <= 0)
                return false;

            var nextIndex = _activeActionIndex == NoSelection
                ? (direction >= 0 ? 0 : item.ActionCount - 1)
                : _activeActionIndex + direction;

            if (WrapNavigation)
            {
                nextIndex = (nextIndex % item.ActionCount + item.ActionCount) % item.ActionCount;
            }
            else if (nextIndex < 0 || nextIndex >= item.ActionCount)
            {
                PlaySfx(_edgeSound);
                return true;
            }

            _activeActionIndex = nextIndex;
            if (item.TryGetActionLabel(_activeActionIndex, out var label) && !string.IsNullOrWhiteSpace(label))
            {
                PlayNavigateSound();
                _speech.Speak(label, SpeechService.SpeakFlag.NoInterruptButStop);
                CancelHint();
            }
            else
            {
                PlayNavigateSound();
            }

            return true;
        }

        private static MenuAdjustAction? GetAdjustmentAction(bool moveLeft, bool moveRight, bool pageUp, bool pageDown, bool moveHome, bool moveEnd)
        {
            if (moveLeft)
                return MenuAdjustAction.Decrease;
            if (moveRight)
                return MenuAdjustAction.Increase;
            if (pageUp)
                return MenuAdjustAction.PageIncrease;
            if (pageDown)
                return MenuAdjustAction.PageDecrease;
            if (moveHome)
                return MenuAdjustAction.ToMaximum;
            if (moveEnd)
                return MenuAdjustAction.ToMinimum;
            return null;
        }
    }
}

