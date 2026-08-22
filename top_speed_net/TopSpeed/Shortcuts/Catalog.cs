using System;
using System.Collections.Generic;
using System.Text;
using Key = TopSpeed.Input.InputKey;
using TopSpeed.Input;
using TopSpeed.Localization;

namespace TopSpeed.Shortcuts
{
    internal sealed class ShortcutCatalog
    {
        private const string GlobalGroupId = "global";
        private const string ScopeGroupPrefix = "scope:";
        private const string MenuGroupPrefix = "menu:";
        private const string ViewGroupPrefix = "view:";

        private readonly Dictionary<string, ShortcutAction> _actions =
            new Dictionary<string, ShortcutAction>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _scopeActionIds =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _menuActionIds =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _viewActionIds =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _menuScopeIds =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _scopeNames =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _menuNames =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _viewNames =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _globalActionIds = new List<string>();

        public void RegisterAction(
            string actionId,
            string displayName,
            string description,
            Key key,
            ShortcutModifiers modifiers,
            Action onTrigger,
            Func<bool>? canExecute = null,
            GestureIntent? gestureIntent = null)
        {
            var action = new ShortcutAction(actionId, displayName, description, key, modifiers, onTrigger, canExecute, gestureIntent);
            _actions[action.Id] = action;
        }

        public void SetBinding(string actionId, Key key, ShortcutModifiers modifiers)
        {
            var normalized = NormalizeRequiredId(actionId, nameof(actionId));
            if (!_actions.TryGetValue(normalized, out var action))
                throw new InvalidOperationException($"Shortcut action '{normalized}' is not registered.");

            action.SetBinding(key, modifiers);
        }

        public void SetGlobalActions(IEnumerable<string>? actionIds)
        {
            SetActionList(_globalActionIds, actionIds);
        }

        public void SetScopeActions(string scopeId, IEnumerable<string>? actionIds, string? scopeName = null)
        {
            SetActionMap(_scopeActionIds, _scopeNames, scopeId, actionIds, scopeName);
        }

        public void SetMenuActions(string menuId, IEnumerable<string>? actionIds, string? menuName = null)
        {
            SetActionMap(_menuActionIds, _menuNames, menuId, actionIds, menuName);
        }

        public void SetViewActions(string viewId, IEnumerable<string>? actionIds, string? viewName = null)
        {
            SetActionMap(_viewActionIds, _viewNames, viewId, actionIds, viewName);
        }

        public void SetMenuScopes(string menuId, IEnumerable<string>? scopeIds)
        {
            SetIdMap(_menuScopeIds, menuId, scopeIds);
        }

        public bool TryResolveTriggeredAction(IInputService input, in ShortcutContext context, out ShortcutAction action)
        {
            action = null!;
            if (input == null)
                return false;

            var candidateIds = BuildCandidateActionIds(in context);
            for (var i = 0; i < candidateIds.Count; i++)
            {
                var actionId = candidateIds[i];
                if (!_actions.TryGetValue(actionId, out var candidate))
                    continue;

                var pressedByKeyboard = IsPressedByKeyboard(input, candidate);
                var pressedByGesture = candidate.GestureIntent.HasValue &&
                    input.WasGesturePressed(candidate.GestureIntent.Value);
                if (!pressedByKeyboard && !pressedByGesture)
                    continue;
                if (!candidate.CanExecute())
                    continue;

                action = candidate;
                return true;
            }

            return false;
        }

        public bool TryResolveTriggeredActionById(IInputService input, string actionId, out ShortcutAction action)
        {
            action = null!;
            if (input == null || string.IsNullOrWhiteSpace(actionId))
                return false;

            var normalized = actionId.Trim();
            if (!_actions.TryGetValue(normalized, out var candidate))
                return false;
            if (!candidate.CanExecute())
                return false;

            var pressedByKeyboard = IsPressedByKeyboard(input, candidate);
            var pressedByGesture = candidate.GestureIntent.HasValue &&
                input.WasGesturePressed(candidate.GestureIntent.Value);
            if (!pressedByKeyboard && !pressedByGesture)
                return false;

            action = candidate;
            return true;
        }

        public IReadOnlyList<ShortcutGroup> GetGroups()
        {
            var groups = new List<ShortcutGroup>
            {
                new ShortcutGroup(GlobalGroupId, LocalizationService.Mark("Global shortcuts"), isGlobal: true)
            };

            AddGroups(groups, ScopeGroupPrefix, _scopeActionIds, _scopeNames);
            AddGroups(groups, MenuGroupPrefix, _menuActionIds, _menuNames);
            AddGroups(groups, ViewGroupPrefix, _viewActionIds, _viewNames);

            groups.Sort((left, right) =>
            {
                if (left.IsGlobal && !right.IsGlobal)
                    return -1;
                if (!left.IsGlobal && right.IsGlobal)
                    return 1;
                return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            });

            return groups;
        }

        public IReadOnlyList<ShortcutBinding> GetGroupBindings(string groupId)
        {
            if (!TryGetGroupActionIds(groupId, out var actionIds))
                return Array.Empty<ShortcutBinding>();

            var bindings = new List<ShortcutBinding>(actionIds.Count);
            for (var i = 0; i < actionIds.Count; i++)
            {
                var actionId = actionIds[i];
                if (!_actions.TryGetValue(actionId, out var action))
                    continue;

                bindings.Add(CreateBinding(action));
            }

            return bindings;
        }

        public bool TryGetBinding(string actionId, out ShortcutBinding binding)
        {
            binding = default;
            var normalizedActionId = NormalizeRequiredId(actionId, nameof(actionId));
            if (!_actions.TryGetValue(normalizedActionId, out var action))
                return false;

            binding = CreateBinding(action);
            return true;
        }

        public bool HasUnmodifiedBindingForKeyInContext(Key key, in ShortcutContext context)
        {
            if (key == Key.Unknown)
                return false;

            var candidateIds = BuildCandidateActionIds(in context);
            for (var i = 0; i < candidateIds.Count; i++)
            {
                var actionId = candidateIds[i];
                if (!_actions.TryGetValue(actionId, out var action))
                    continue;

                if (action.Key == key && action.Modifiers.IsEmpty)
                    return true;
            }

            return false;
        }

        // True when some registered shortcut claims this key with modifiers that are held right now.
        // Shortcuts and drive intents are evaluated by separate readers of the same keyboard, so
        // without asking this a combination fires its own action and the bare-key intent underneath it
        // at the same time - Control+Shift+C toggles transmission and also reads the distance travelled
        // off the C bound to Report distance. Every registered binding is considered rather than only
        // the ones reachable in the current menu context, because the ones that collide here are the
        // globally dispatched ones that fire with no menu on the stack at all.
        public bool HasHeldModifierBindingForKey(Key key, IInputService input)
        {
            if (key == Key.Unknown || input == null)
                return false;

            foreach (var pair in _actions)
            {
                var action = pair.Value;
                if (action.Key != key || action.Modifiers.IsEmpty)
                    continue;
                if (action.Modifiers.MatchesInput(input))
                    return true;
            }

            return false;
        }

        public bool IsBindingInUseInGroup(string groupId, Key key, ShortcutModifiers modifiers, string ignoredActionId)
        {
            if (!TryGetGroupActionIds(groupId, out var actionIds))
                return false;

            var ignoredId = string.IsNullOrWhiteSpace(ignoredActionId)
                ? string.Empty
                : ignoredActionId.Trim();
            for (var i = 0; i < actionIds.Count; i++)
            {
                var actionId = actionIds[i];
                if (!string.IsNullOrWhiteSpace(ignoredId) && string.Equals(actionId, ignoredId, StringComparison.Ordinal))
                    continue;
                if (!_actions.TryGetValue(actionId, out var action))
                    continue;
                if (action.Key == key && action.Modifiers.Equals(modifiers))
                    return true;
            }

            return false;
        }

        public void ResetBindings()
        {
            foreach (var pair in _actions)
                pair.Value.ResetKey();
        }

        public bool ResetBindingsInGroup(string groupId)
        {
            if (!TryGetGroupActionIds(groupId, out var actionIds))
                return false;
            if (actionIds.Count == 0)
                return false;

            var resetAny = false;
            for (var i = 0; i < actionIds.Count; i++)
            {
                var actionId = actionIds[i];
                if (!_actions.TryGetValue(actionId, out var action))
                    continue;

                action.ResetKey();
                resetAny = true;
            }

            return resetAny;
        }

        private List<string> BuildCandidateActionIds(in ShortcutContext context)
        {
            var resolved = new List<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);

            AddActionIds(_viewActionIds, context.ViewId, resolved, unique);
            AddActionIds(_menuActionIds, context.MenuId, resolved, unique);

            if (_menuScopeIds.TryGetValue(context.MenuId, out var scopeIds))
            {
                for (var i = 0; i < scopeIds.Count; i++)
                    AddActionIds(_scopeActionIds, scopeIds[i], resolved, unique);
            }

            for (var i = 0; i < _globalActionIds.Count; i++)
            {
                var actionId = _globalActionIds[i];
                if (unique.Add(actionId))
                    resolved.Add(actionId);
            }

            return resolved;
        }

        private static void AddActionIds(
            Dictionary<string, List<string>> source,
            string ownerId,
            List<string> resolved,
            HashSet<string> unique)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
                return;
            if (!source.TryGetValue(ownerId, out var actionIds))
                return;

            for (var i = 0; i < actionIds.Count; i++)
            {
                var actionId = actionIds[i];
                if (unique.Add(actionId))
                    resolved.Add(actionId);
            }
        }

        private static void SetActionMap(
            Dictionary<string, List<string>> target,
            Dictionary<string, string> names,
            string ownerId,
            IEnumerable<string>? actionIds,
            string? ownerName = null)
        {
            var normalizedOwnerId = NormalizeRequiredId(ownerId, nameof(ownerId));
            var normalizedActionIds = NormalizeIds(actionIds);
            if (normalizedActionIds.Count == 0)
            {
                target.Remove(normalizedOwnerId);
                names.Remove(normalizedOwnerId);
                return;
            }

            target[normalizedOwnerId] = normalizedActionIds;
            SetName(names, normalizedOwnerId, ownerName);
        }

        private static void SetIdMap(
            Dictionary<string, List<string>> target,
            string ownerId,
            IEnumerable<string>? ids)
        {
            var normalizedOwnerId = NormalizeRequiredId(ownerId, nameof(ownerId));
            var normalizedIds = NormalizeIds(ids);
            if (normalizedIds.Count == 0)
            {
                target.Remove(normalizedOwnerId);
                return;
            }

            target[normalizedOwnerId] = normalizedIds;
        }

        private static void SetActionList(List<string> target, IEnumerable<string>? actionIds)
        {
            target.Clear();
            var normalizedActionIds = NormalizeIds(actionIds);
            if (normalizedActionIds.Count == 0)
                return;

            target.AddRange(normalizedActionIds);
        }

        private static List<string> NormalizeIds(IEnumerable<string>? ids)
        {
            var normalized = new List<string>();
            if (ids == null)
                return normalized;

            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var candidate = id.Trim();
                if (!unique.Add(candidate))
                    continue;

                normalized.Add(candidate);
            }

            return normalized;
        }

        private static string NormalizeRequiredId(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Identifier is required.", paramName);
            return value.Trim();
        }

        private static void SetName(Dictionary<string, string> names, string ownerId, string? ownerName)
        {
            if (ownerName == null)
                return;

            var normalizedName = ownerName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
                names.Remove(ownerId);
            else
                names[ownerId] = normalizedName;
        }

        private static void AddGroups(
            List<ShortcutGroup> target,
            string prefix,
            Dictionary<string, List<string>> source,
            Dictionary<string, string> names)
        {
            foreach (var pair in source)
            {
                if (pair.Value.Count == 0)
                    continue;

                var ownerId = pair.Key;
                var groupId = $"{prefix}{ownerId}";
                var groupName = names.TryGetValue(ownerId, out var customName)
                    ? customName
                    : FormatName(ownerId);
                target.Add(new ShortcutGroup(groupId, groupName, isGlobal: false));
            }
        }

        private bool TryGetGroupActionIds(string groupId, out List<string> actionIds)
        {
            actionIds = null!;
            var normalizedGroupId = NormalizeRequiredId(groupId, nameof(groupId));
            if (string.Equals(normalizedGroupId, GlobalGroupId, StringComparison.Ordinal))
            {
                actionIds = _globalActionIds;
                return true;
            }

            if (TryGetOwnerId(normalizedGroupId, ScopeGroupPrefix, out var scopeId) &&
                _scopeActionIds.TryGetValue(scopeId, out var scopeActionIds) &&
                scopeActionIds != null)
            {
                actionIds = scopeActionIds;
                return true;
            }

            if (TryGetOwnerId(normalizedGroupId, MenuGroupPrefix, out var menuId) &&
                _menuActionIds.TryGetValue(menuId, out var menuActionIds) &&
                menuActionIds != null)
            {
                actionIds = menuActionIds;
                return true;
            }

            if (TryGetOwnerId(normalizedGroupId, ViewGroupPrefix, out var viewId) &&
                _viewActionIds.TryGetValue(viewId, out var viewActionIds) &&
                viewActionIds != null)
            {
                actionIds = viewActionIds;
                return true;
            }

            return false;
        }

        private static bool TryGetOwnerId(string groupId, string prefix, out string ownerId)
        {
            ownerId = string.Empty;
            if (!groupId.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            ownerId = groupId.Substring(prefix.Length);
            return !string.IsNullOrWhiteSpace(ownerId);
        }

        private static ShortcutBinding CreateBinding(ShortcutAction action)
        {
            return new ShortcutBinding(action.Id, action.DisplayName, action.Description, action.Key, action.Modifiers, action.GestureIntent);
        }

        private static bool IsPressedByKeyboard(IInputService input, ShortcutAction candidate)
        {
            if (!candidate.Modifiers.MatchesInput(input))
                return false;

            return input.WasPressed(candidate.Key);
        }

        private static string FormatName(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return string.Empty;

            var builder = new StringBuilder(source.Length);
            var newWord = true;
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                if (c == '_' || c == '-' || c == ':' || c == '.')
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
                        builder.Append(' ');
                    newWord = true;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
                        builder.Append(' ');
                    newWord = true;
                    continue;
                }

                builder.Append(newWord ? char.ToUpperInvariant(c) : c);
                newWord = false;
            }

            var result = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? source.Trim() : result;
        }
    }
}



