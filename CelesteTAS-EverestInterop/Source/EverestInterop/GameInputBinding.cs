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
            { Actions.Up,       KeyCode.UpArrow },
            { Actions.Down,     KeyCode.DownArrow },
            { Actions.Left,     KeyCode.LeftArrow },
            { Actions.Right,    KeyCode.RightArrow },
            { Actions.Jump,     KeyCode.Z },
            { Actions.Dash,     KeyCode.C },
            { Actions.DashOnly, KeyCode.X },
        };
    }

#if UNITY_NEW_INPUT_SYSTEM
    private static KeyboardState ToKeyboardState(InputFrame frame) {
        var a = frame.Actions;
        var state = new KeyboardState();

        if (a.HasFlag(Actions.Up))       state.Press(NisKey.UpArrow);
        if (a.HasFlag(Actions.Down))     state.Press(NisKey.DownArrow);
        if (a.HasFlag(Actions.Left))     state.Press(NisKey.LeftArrow);
        if (a.HasFlag(Actions.Right))    state.Press(NisKey.RightArrow);
        if (a.HasFlag(Actions.Jump))     state.Press(NisKey.Z);
        if (a.HasFlag(Actions.Dash))     state.Press(NisKey.C);
        if (a.HasFlag(Actions.DashOnly)) state.Press(NisKey.X);

        return state;
    }
#endif
}

