using StudioCommunication;
using System.Collections.Generic;
using TAS.Input;
using UnityEngine;
#if UNITY_NEW_INPUT_SYSTEM
using UnityEngine.InputSystem.LowLevel;
using NisKey = UnityEngine.InputSystem.Key;
#endif

namespace TAS.EverestInterop;

// Silksong input mapping for both input systems.
// Default keyboard bindings from InputHandler.SetupDefaultKeyboardBindings.
internal static class GameInputBinding {
    [Initialize]
    private static void Initialize() {
        // New Input System (virtual keyboard)
#if UNITY_NEW_INPUT_SYSTEM
        NewInputSystemInjector.BuildState = ToKeyboardState;
#endif

        // Legacy Input System (UnityEngine.Input.GetKey patches)
        InputHelper.OldInputSystemActionKeyMap = new Dictionary<Actions, KeyCode> {
            { Actions.Up,    KeyCode.W },
            { Actions.Down,  KeyCode.S },
            { Actions.Left,  KeyCode.A },
            { Actions.Right, KeyCode.D },
            { Actions.Jump,  KeyCode.Space },
            { Actions.Dash,  KeyCode.LeftShift },
        };
    }

#if UNITY_NEW_INPUT_SYSTEM
    private static KeyboardState ToKeyboardState(InputFrame frame) {
        var a = frame.Actions;
        var state = new KeyboardState();

        if (a.HasFlag(Actions.Up))    state.Press(NisKey.W);
        if (a.HasFlag(Actions.Down))  state.Press(NisKey.S);
        if (a.HasFlag(Actions.Left))  state.Press(NisKey.A);
        if (a.HasFlag(Actions.Right)) state.Press(NisKey.D);
        if (a.HasFlag(Actions.Jump))  state.Press(NisKey.Space);
        if (a.HasFlag(Actions.Dash))  state.Press(NisKey.LeftShift);

        return state;
    }
#endif
}

