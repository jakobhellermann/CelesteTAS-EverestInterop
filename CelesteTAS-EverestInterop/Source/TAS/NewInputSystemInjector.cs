#if UNITY_NEW_INPUT_SYSTEM
using System;
using TAS.Input;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace TAS;

public static class NewInputSystemInjector {
    // Set by per-game interop. Null = New Input System injection disabled.
    public static Func<InputFrame, KeyboardState>? BuildState;

    public static Keyboard? VirtualKeyboard { get; private set; }

    private static Keyboard? _previousCurrent;

    [EnableRun]
    private static void EnableRun() {
        if (BuildState == null) return;

        _previousCurrent = Keyboard.current;
        VirtualKeyboard = InputSystem.AddDevice<Keyboard>("TasKeyboard");
        VirtualKeyboard.MakeCurrent();
        InputSystem.onEvent += SuppressRealKeyboard;
    }

    [DisableRun]
    private static void DisableRun() {
        if (VirtualKeyboard == null) return;

        InputSystem.onEvent -= SuppressRealKeyboard;
        InputSystem.RemoveDevice(VirtualKeyboard);
        VirtualKeyboard = null;
        _previousCurrent?.MakeCurrent();
        _previousCurrent = null;
    }

    public static void FeedFrame(InputFrame frame) {
        if (VirtualKeyboard == null || BuildState == null) return;

        var state = BuildState(frame);
        InputState.Change(VirtualKeyboard, state);
    }

    private static void SuppressRealKeyboard(InputEventPtr ev, InputDevice device) {
        if (device == VirtualKeyboard) return;
        if (device is Keyboard) ev.handled = true;
    }
}
#endif
