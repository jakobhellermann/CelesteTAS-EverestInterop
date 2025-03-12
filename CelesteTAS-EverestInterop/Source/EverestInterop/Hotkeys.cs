using System;
using System.Collections.Generic;
using System.Linq;
using InControl;
using MeshMorpher;
using Microsoft.Xna.Framework.Input;
using StudioCommunication;
using TAS.Communication;
using TAS.Utils;
using UnityEngine;
using UnityEngine.UIElements;
using XInputDotNetPure;

using InputKeys = UnityEngine.KeyCode;
using InputButtons = int;


namespace TAS.UnityInterop;


public static class Hotkeys {
    // private static readonly Lazy<FieldInfo?> f_CelesteNetClientModule_Instance = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Instance"));
    // private static readonly Lazy<FieldInfo?> f_CelesteNetClientModule_Context = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Context"));
    // private static readonly Lazy<FieldInfo?> f_CelesteNetClientContext_Chat = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientContext")?.GetFieldInfo("Chat"));
    // private static readonly Lazy<PropertyInfo?> p_CelesteNetChatComponent_Active = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.Components.CelesteNetChatComponent")?.GetPropertyInfo("Active"));

    // private static KeyboardState kbState;
    private static GamePadState padState;

    public static Hotkey StartStop { get; private set; } = null!;
    public static Hotkey Restart { get; private set; } = null!;
    public static Hotkey FastForward { get; private set; } = null!;
    public static Hotkey FastForwardComment { get; private set; } = null!;
    public static Hotkey SlowForward { get; private set; } = null!;
    public static Hotkey FrameAdvance { get; private set; } = null!;
    public static Hotkey PauseResume { get; private set; } = null!;
    public static Hotkey Hitboxes { get; private set; } = null!;
    public static Hotkey TriggerHitboxes { get; private set; } = null!;
    public static Hotkey SimplifiedGraphic { get; private set; } = null!;
    public static Hotkey CenterCamera { get; private set; } = null!;
    public static Hotkey LockCamera { get; private set; } = null!;
    public static Hotkey SaveState { get; private set; } = null!;
    public static Hotkey ClearState { get; private set; } = null!;
    public static Hotkey InfoHud { get; private set; } = null!;
    public static Hotkey FreeCamera { get; private set; } = null!;
    public static Hotkey CameraUp { get; private set; } = null!;
    public static Hotkey CameraDown { get; private set; } = null!;
    public static Hotkey CameraLeft { get; private set; } = null!;
    public static Hotkey CameraRight { get; private set; } = null!;
    public static Hotkey CameraZoomIn { get; private set; } = null!;
    public static Hotkey CameraZoomOut { get; private set; } = null!;
    public static Hotkey OpenConsole { get; private set; } = null!;

    // public static float RightThumbSticksX => padState.ThumbSticks.Right.X;

    public static readonly Dictionary<HotkeyID, Hotkey> AllHotkeys = new();
    public static Dictionary<HotkeyID, List<InputKeys>> StudioHotkeys = new();

    /// Hotkeys which shouldn't be triggered from Studio
    private static readonly List<HotkeyID> StudioIgnoreHotkeys = [
        HotkeyID.InfoHud,
        HotkeyID.FreeCamera,
        HotkeyID.CameraUp, HotkeyID.CameraDown, HotkeyID.CameraLeft, HotkeyID.CameraRight,
        HotkeyID.CameraZoomIn, HotkeyID.CameraZoomOut,
        HotkeyID.OpenConsole
    ];

    /// Checks if the CelesteNet chat is open
    /*private static bool CelesteNetChatting {
        get {
            if (f_CelesteNetClientModule_Instance.Value?.GetValue(null) is not { } instance) {
                return false;
            }
            if (f_CelesteNetClientModule_Context.Value?.GetValue(instance) is not { } context) {
                return false;
            }
            if (f_CelesteNetClientContext_Chat.Value?.GetValue(context) is not { } chat) {
                return false;
            }

            return p_CelesteNetChatComponent_Active.Value?.GetValue(chat) as bool? == true;
        }
    }*/

    internal static bool Initialized { get; private set; } = false;

    [Initialize]
    private static void Initialize() {
        AllHotkeys.Clear();
        AllHotkeys[HotkeyID.Start] = StartStop = Unbound();
        AllHotkeys[HotkeyID.Restart] = Restart = Unbound();
        AllHotkeys[HotkeyID.FastForward] = FastForward = Unbound();
        AllHotkeys[HotkeyID.FastForwardComment] = FastForwardComment = Unbound();
        AllHotkeys[HotkeyID.FrameAdvance] = FrameAdvance = Unbound();
        AllHotkeys[HotkeyID.SlowForward] = SlowForward = Unbound();
        AllHotkeys[HotkeyID.Pause] = PauseResume = Unbound();
        AllHotkeys[HotkeyID.Hitboxes] = Hitboxes = Unbound();
        AllHotkeys[HotkeyID.TriggerHitboxes] = TriggerHitboxes = Unbound();
        AllHotkeys[HotkeyID.Graphics] = SimplifiedGraphic = Unbound();
        AllHotkeys[HotkeyID.Camera] = CenterCamera = Unbound();
        AllHotkeys[HotkeyID.LockCamera] = LockCamera = Unbound();
        AllHotkeys[HotkeyID.SaveState] = SaveState = Unbound();
        AllHotkeys[HotkeyID.ClearState] = ClearState = Unbound();
        AllHotkeys[HotkeyID.InfoHud] = InfoHud = Unbound();
        AllHotkeys[HotkeyID.FreeCamera] = FreeCamera = Unbound();
        AllHotkeys[HotkeyID.CameraUp] = CameraUp = Unbound();
        AllHotkeys[HotkeyID.CameraDown] = CameraDown = Unbound();
        AllHotkeys[HotkeyID.CameraLeft] = CameraLeft = Unbound();
        AllHotkeys[HotkeyID.CameraRight] = CameraRight = Unbound();
        AllHotkeys[HotkeyID.CameraZoomIn] = CameraZoomIn = Unbound();
        AllHotkeys[HotkeyID.CameraZoomOut] = CameraZoomOut = Unbound();

        /*var debugConsole = Celeste.Mod.Core.CoreModule.Settings.DebugConsole;
        var toggleDebugConsole = Celeste.Mod.Core.CoreModule.Settings.ToggleDebugConsole;*
        AllHotkeys[HotkeyID.OpenConsole] = OpenConsole = new Hotkey(
            debugConsole.Keys.Union(toggleDebugConsole.Keys).ToList(),
            debugConsole.Buttons.Union(toggleDebugConsole.Buttons).ToList(),
            keyCombo: false, held: false);
            */
        AllHotkeys[HotkeyID.OpenConsole] = OpenConsole = Unbound();

        // Respond to rebinding
        /*Everest.Events.Input.OnInitialize += () => {
            debugConsole = Celeste.Mod.Core.CoreModule.Settings.DebugConsole;
            toggleDebugConsole = Celeste.Mod.Core.CoreModule.Settings.ToggleDebugConsole;

            AllHotkeys[HotkeyID.OpenConsole].Keys.Clear();
            AllHotkeys[HotkeyID.OpenConsole].Keys.AddRange(debugConsole.Keys);
            AllHotkeys[HotkeyID.OpenConsole].Keys.AddRange(toggleDebugConsole.Keys);

            AllHotkeys[HotkeyID.OpenConsole].Buttons.Clear();
            AllHotkeys[HotkeyID.OpenConsole].Buttons.AddRange(debugConsole.Buttons);
            AllHotkeys[HotkeyID.OpenConsole].Buttons.AddRange(toggleDebugConsole.Buttons);
        };*/

        StudioHotkeys = AllHotkeys
            .Where(entry => !StudioIgnoreHotkeys.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value.Keys);

        Initialized = true;

        CommunicationWrapper.SendCurrentBindings();

        return;

        // static Hotkey BindingToHotkey(ButtonBinding binding, bool held = false) {
            // return new(binding.Keys, binding.Buttons, true, held);
        // }
        static Hotkey Unbound() {
            return new ([], [], false, false);
        }
    }

    private static GamePadState GetGamePadState() {
        for (int i = 0; i < 4; i++) {
            var state = GamePad.GetState((PlayerIndex) i);
            if (state.IsConnected) {
                return state;
            }
        }

        // No controller connected
        return default;
    }
    internal static void UpdateMeta() {
        // Only update if the keys aren't already used for something else
        bool updateKey = true, updateButton = true;

        // Prevent triggering hotkeys while writing text
        /*if (Engine.Commands.Open) {
            updateKey = false;
        } else if (TextInput.Initialized && typeof(TextInput).GetFieldValue<Action<char>>("_OnInput") is { } inputEvent) {
            // ImGuiHelper is always subscribed, so ignore it
            updateKey &= inputEvent.GetInvocationList().All(d => d.Target?.GetType().FullName == "Celeste.Mod.ImGuiHelper.ImGuiRenderer+<>c");
        }

        if (!Manager.Running) {
            if (CelesteNetChatting) {
                updateKey = false;
            }

            if (Engine.Scene?.Tracker is { } tracker) {
                if (tracker.GetEntity<KeyboardConfigUI>() != null) {
                    updateKey = false;
                }
                if (tracker.GetEntity<ButtonConfigUI>() != null) {
                    updateButton = false;
                }
            }
        }
        */

        padState = GetGamePadState();
        foreach (var hotkey in AllHotkeys.Values) {
            if (hotkey == InfoHud) {
                hotkey.Update(); // Always update Info HUD
            } else if (hotkey == OpenConsole) {
                hotkey.Update(true, updateButton); // Keep updating Open Console hotkey while console is open
            } else {
                hotkey.Update(updateKey, updateButton);
            }
        }

        // React to hotkeys
        AfterUpdate();
    }

    private static void AfterUpdate() {
        /*
         if (Engine.Scene is Level level && (!level.Paused || level.PauseMainMenuOpen || Manager.Running)) {
            if (Hitboxes.Pressed) {
                TasSettings.ShowHitboxes = !TasSettings.ShowHitboxes;
                CelesteTasModule.Instance.SaveSettings();
            }

            if (TriggerHitboxes.Pressed) {
                TasSettings.ShowTriggerHitboxes = !TasSettings.ShowTriggerHitboxes;
                CelesteTasModule.Instance.SaveSettings();
            }

            if (SimplifiedGraphic.Pressed) {
                TasSettings.SimplifiedGraphics = !TasSettings.SimplifiedGraphics;
                CelesteTasModule.Instance.SaveSettings();
            }

            if (CenterCamera.Pressed) {
                TasSettings.CenterCamera = !TasSettings.CenterCamera;
                CelesteTasModule.Instance.SaveSettings();
            }
        }

        Hud.Toggle();
        */
    }

    [DisableRun]
    private static void ReleaseAllKeys() {
        foreach (Hotkey hotkey in AllHotkeys.Values) {
            hotkey.OverrideCheck = false;
        }
    }

    /// Hotkey which is independent of the game Update loop
    public class Hotkey(List<InputKeys> keys, List<InputButtons> buttons, bool keyCombo, bool held) {
        public readonly List<InputKeys> Keys = keys;
        public readonly List<InputButtons> Buttons = buttons;

        internal bool OverrideCheck;

        private DateTime doublePressTimeout;
        private DateTime repeatTimeout;

        public bool Check { get; private set; }
        public bool Pressed => !LastCheck && Check;
        public bool Released => LastCheck && !Check;

        public bool DoublePressed { get; private set; }
        public bool Repeated { get; private set; }

        public bool LastCheck { get; set; }

        internal const double DoublePressTimeoutMS = 200.0;
        internal const double RepeatTimeoutMS = 500.0;

        internal void Update(bool updateKey = true, bool updateButton = true) {
            LastCheck = Check;

            bool keyCheck;
            bool buttonCheck;

            if (OverrideCheck) {
                keyCheck = buttonCheck = true;
                if (!held) {
                    OverrideCheck = false;
                }
            } else {
                keyCheck = updateKey && IsKeyDown();
                buttonCheck = updateButton && IsButtonDown();
            }

            Check = keyCheck || buttonCheck;

            var now = DateTime.Now;
            if (Pressed) {
                DoublePressed = now < doublePressTimeout;
                doublePressTimeout = DoublePressed ? default : now + TimeSpan.FromMilliseconds(DoublePressTimeoutMS);

                Repeated = true;
                repeatTimeout = now + TimeSpan.FromMilliseconds(RepeatTimeoutMS);
            } else if (Check) {
                DoublePressed = false;
                Repeated = now >= repeatTimeout;
            } else {
                DoublePressed = false;
                Repeated = false;
                repeatTimeout = default;
            }
        }

        private bool IsKeyDown() {
            if (Keys.Count == 0) {
                return false;
            }

            return keyCombo ? Keys.All(UnityEngine.Input.GetKey) : Keys.Any(UnityEngine.Input.GetKey);
        }
        private bool IsButtonDown() {
            if (Buttons.Count == 0) {
                return false;
            }

            return keyCombo ? Buttons.All(UnityEngine.Input.GetMouseButton) : Buttons.Any(UnityEngine.Input.GetMouseButton);
        }

        public override string ToString() {
            List<string> result = new();
            if (Keys.IsNotEmpty()) {
                result.Add(string.Join("+", Keys.Select(key => key.ToString())));
            }

            if (Buttons.IsNotEmpty()) {
                result.Add(string.Join("+", Buttons));
            }

            return string.Join("/", result);
        }
    }
}

/// Manages hotkeys for controlling TAS playback
/// Cannot use MInput, since that isn't updated while paused and already used for TAS inputs
internal static class MouseInput {
    public class Button {
        private DateTime doublePressTimeout;
        private DateTime repeatTimeout;

        public bool Check { get; private set; }
        public bool Pressed => !LastCheck && Check;
        public bool Released => LastCheck && !Check;

        public bool DoublePressed { get; private set; }
        public bool Repeated { get; private set; }

        public bool LastCheck { get; set; }

        public void Update(ButtonState state) {
            LastCheck = Check;
            Check = state == ButtonState.Pressed;

            var now = DateTime.Now;
            if (Pressed) {
                DoublePressed = now < doublePressTimeout;
                doublePressTimeout = DoublePressed ? default : now + TimeSpan.FromMilliseconds(Hotkeys.Hotkey.DoublePressTimeoutMS);

                Repeated = true;
                repeatTimeout = now + TimeSpan.FromMilliseconds(Hotkeys.Hotkey.RepeatTimeoutMS);
            } else if (Check) {
                DoublePressed = false;
                Repeated = now >= repeatTimeout;
            } else {
                DoublePressed = false;
                Repeated = false;
                repeatTimeout = default;
            }
        }
    }

    public static bool Updating { get; private set; }

    public static Vector2 Position { get; private set; }
    public static Vector2 PositionDelta => Position - lastPosition;
    private static Vector2 lastPosition;

    public static float WheelDelta { get; private set; }
    private static float lastWheel;

    public static readonly Button Left = new();
    public static readonly Button Middle = new();
    public static readonly Button Right = new();

    [UpdateMeta]
    private static void UpdateMeta() {
        // Avoid checking mouse inputs while fast forwarding for performance
        if (Manager.FastForwarding) {
            lastPosition = Position;
            Left.Update(ButtonState.Released);
            Middle.Update(ButtonState.Released);
            Right.Update(ButtonState.Released);
            WheelDelta = 0;

            return;
        }

        Updating = true;
        var mouseDelta = UnityEngine.Input.mouseScrollDelta.x;
        Updating = false;

        lastPosition = Position;
        Position = UnityEngine.Input.mousePosition;

        WheelDelta = mouseDelta - lastWheel;
        lastWheel = mouseDelta;

        ;
        Left.Update(UnityEngine.Input.GetMouseButton(0) ? ButtonState.Pressed : ButtonState.Released);
        Middle.Update(UnityEngine.Input.GetMouseButton(2) ? ButtonState.Pressed : ButtonState.Released);
        Right.Update(UnityEngine.Input.GetMouseButton(1) ? ButtonState.Pressed : ButtonState.Released);
    }
}
