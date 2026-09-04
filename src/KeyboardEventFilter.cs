using System;
using System.Collections.Generic;

namespace KeyboardDebounce
{
    internal sealed class KeyboardFilterResult
    {
        public bool Suppress { get; set; }
        public int VirtualKeyCode { get; set; }
        public DebounceDecision Decision { get; set; }
    }

    internal sealed class DeliveredKeyState
    {
        public bool LogicalDownDelivered;
        public bool SuppressedDownWithoutLogicalDown;
        public bool BypassUntilPhysicalKeyUp;
    }

    internal sealed class KeyboardEventFilter
    {
        private readonly DebounceEngine _engine;
        private readonly Func<bool> _isEnabled;
        private readonly Func<bool> _isGameModeActive;
        private readonly Dictionary<PhysicalKeyIdentity, DeliveredKeyState> _delivered;
        private bool _hasObservedMode;
        private bool _lastEnabled;
        private bool _lastGameModeActive;

        public KeyboardEventFilter(DebounceEngine engine, Func<bool> isEnabled)
            : this(engine, isEnabled, delegate { return false; })
        {
        }

        public KeyboardEventFilter(
            DebounceEngine engine,
            Func<bool> isEnabled,
            Func<bool> isGameModeActive)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            if (isEnabled == null) throw new ArgumentNullException("isEnabled");
            if (isGameModeActive == null) throw new ArgumentNullException("isGameModeActive");
            _engine = engine;
            _isEnabled = isEnabled;
            _isGameModeActive = isGameModeActive;
            _delivered = new Dictionary<PhysicalKeyIdentity, DeliveredKeyState>();
        }

        public KeyboardFilterResult Process(
            int virtualKeyCode,
            uint scanCode,
            uint hookFlags,
            bool isKeyDown,
            long timestampMs)
        {
            if (IsInjected(hookFlags))
            {
                return Pass(virtualKeyCode, null);
            }

            bool enabled = _isEnabled();
            bool gameModeActive = _isGameModeActive();
            ObserveInputMode(enabled, gameModeActive);

            if (IsModifierKey(virtualKeyCode))
            {
                return Pass(virtualKeyCode, null);
            }

            var identity = new PhysicalKeyIdentity(
                virtualKeyCode,
                scanCode,
                (hookFlags & NativeMethods.LLKHF_EXTENDED) != 0);
            DeliveredKeyState deliveredState;
            if (_delivered.TryGetValue(identity, out deliveredState)
                && deliveredState.BypassUntilPhysicalKeyUp)
            {
                return ProcessBypassedPhysicalEvent(identity, isKeyDown);
            }

            bool bypass = !enabled
                || _engine.IsIgnored(virtualKeyCode)
                || (gameModeActive && !_engine.IsGameModeFilteredKey(virtualKeyCode));
            if (bypass)
            {
                return ProcessBypassedPhysicalEvent(identity, isKeyDown);
            }

            DebounceDecision decision = _engine.Process(new KeyEventSample
            {
                VirtualKeyCode = virtualKeyCode,
                ScanCode = scanCode,
                IsExtendedKey = identity.IsExtendedKey,
                IsKeyDown = isKeyDown,
                TimestampMs = timestampMs
            }, gameModeActive ? DebounceMode.Game : DebounceMode.Normal);
            if (isKeyDown)
            {
                return ProcessKeyDown(identity, decision);
            }
            return ProcessKeyUp(identity, decision);
        }

        public void ResetRuntimeState()
        {
            PreserveHeldKeysAcrossInputBoundary();
        }

        private void ObserveInputMode(bool enabled, bool gameModeActive)
        {
            if (_hasObservedMode
                && (enabled != _lastEnabled
                    || gameModeActive != _lastGameModeActive))
            {
                PreserveHeldKeysAcrossInputBoundary();
            }

            _hasObservedMode = true;
            _lastEnabled = enabled;
            _lastGameModeActive = gameModeActive;
        }

        private void PreserveHeldKeysAcrossInputBoundary()
        {
            var pendingPhysicalKeyUps = new HashSet<PhysicalKeyIdentity>();
            var statesToRemove = new List<PhysicalKeyIdentity>();
            foreach (var pair in _delivered)
            {
                if (!pair.Value.LogicalDownDelivered
                    && pair.Value.SuppressedDownWithoutLogicalDown)
                {
                    pendingPhysicalKeyUps.Add(pair.Key);
                }
                else if (pair.Value.LogicalDownDelivered)
                {
                    pair.Value.BypassUntilPhysicalKeyUp = true;
                }
                else
                {
                    statesToRemove.Add(pair.Key);
                }
            }

            foreach (PhysicalKeyIdentity identity in statesToRemove)
            {
                _delivered.Remove(identity);
            }
            _engine.ResetRuntimeStateExcept(pendingPhysicalKeyUps);
        }

        private KeyboardFilterResult ProcessBypassedPhysicalEvent(
            PhysicalKeyIdentity identity,
            bool isKeyDown)
        {
            DeliveredKeyState state;
            bool hasState = _delivered.TryGetValue(identity, out state);
            bool hasPendingPhysicalKeyUp = hasState
                && !state.LogicalDownDelivered
                && state.SuppressedDownWithoutLogicalDown;

            if (isKeyDown)
            {
                if (!hasState)
                {
                    state = new DeliveredKeyState();
                    _delivered[identity] = state;
                }
                if (hasPendingPhysicalKeyUp)
                {
                    _engine.CompleteRuntimeKeyUpWithoutLearning(identity);
                }

                state.LogicalDownDelivered = true;
                state.SuppressedDownWithoutLogicalDown = false;
                state.BypassUntilPhysicalKeyUp = true;
                return Pass(identity.VirtualKeyCode, null);
            }

            if (hasState)
            {
                _delivered.Remove(identity);
            }
            _engine.CompleteRuntimeKeyUpWithoutLearning(identity);
            if (!hasPendingPhysicalKeyUp)
            {
                return Pass(identity.VirtualKeyCode, null);
            }

            return PairedKeyUp(identity.VirtualKeyCode, null);
        }

        private KeyboardFilterResult ProcessKeyDown(
            PhysicalKeyIdentity identity,
            DebounceDecision decision)
        {
            DeliveredKeyState state = GetState(identity);
            if (decision.Suppress)
            {
                if (!state.LogicalDownDelivered)
                {
                    state.SuppressedDownWithoutLogicalDown = true;
                }
            }
            else
            {
                state.LogicalDownDelivered = true;
                state.SuppressedDownWithoutLogicalDown = false;
                state.BypassUntilPhysicalKeyUp = false;
            }

            return new KeyboardFilterResult
            {
                Suppress = decision.Suppress,
                VirtualKeyCode = identity.VirtualKeyCode,
                Decision = decision
            };
        }

        private KeyboardFilterResult ProcessKeyUp(
            PhysicalKeyIdentity identity,
            DebounceDecision decision)
        {
            DeliveredKeyState state;
            if (!_delivered.TryGetValue(identity, out state))
            {
                return Pass(identity.VirtualKeyCode, decision);
            }

            bool suppress = !state.LogicalDownDelivered
                && state.SuppressedDownWithoutLogicalDown;
            _delivered.Remove(identity);
            if (suppress)
            {
                return PairedKeyUp(identity.VirtualKeyCode, decision);
            }

            return new KeyboardFilterResult
            {
                Suppress = suppress,
                VirtualKeyCode = identity.VirtualKeyCode,
                Decision = decision
            };
        }

        private static KeyboardFilterResult PairedKeyUp(
            int virtualKeyCode,
            DebounceDecision decision)
        {
            if (decision == null)
            {
                decision = new DebounceDecision
                {
                    LearningReason = "",
                    LearningStateChanged = false
                };
            }
            decision.Suppress = true;
            decision.Reason = "paired-key-up";

            return new KeyboardFilterResult
            {
                Suppress = true,
                VirtualKeyCode = virtualKeyCode,
                Decision = decision
            };
        }

        private DeliveredKeyState GetState(PhysicalKeyIdentity identity)
        {
            DeliveredKeyState state;
            if (!_delivered.TryGetValue(identity, out state))
            {
                state = new DeliveredKeyState();
                _delivered[identity] = state;
            }
            return state;
        }

        private static bool IsInjected(uint flags)
        {
            return (flags & (NativeMethods.LLKHF_INJECTED
                | NativeMethods.LLKHF_LOWER_IL_INJECTED)) != 0;
        }

        private static bool IsModifierKey(int virtualKeyCode)
        {
            switch (virtualKeyCode)
            {
                case 0x10: // VK_SHIFT
                case 0x11: // VK_CONTROL
                case 0x12: // VK_MENU
                case 0x5B: // VK_LWIN
                case 0x5C: // VK_RWIN
                case 0xA0: // VK_LSHIFT
                case 0xA1: // VK_RSHIFT
                case 0xA2: // VK_LCONTROL
                case 0xA3: // VK_RCONTROL
                case 0xA4: // VK_LMENU
                case 0xA5: // VK_RMENU
                    return true;
                default:
                    return false;
            }
        }

        private static KeyboardFilterResult Pass(
            int virtualKeyCode,
            DebounceDecision decision)
        {
            return new KeyboardFilterResult
            {
                Suppress = false,
                VirtualKeyCode = virtualKeyCode,
                Decision = decision
            };
        }
    }
}
