using GlobalEnums;
using HarmonyLib;
using HutongGames.PlayMaker;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TasTracer = TAS.Tracer.TasTracer;
using TAS.InfoHUD;
using TAS.Utils;
using UnityEngine;
#pragma warning disable CS0162 // Unreachable code detected

namespace TAS;

[HarmonyPatch]
[SuppressMessage("Method Declaration", "Harmony003:Harmony non-ref patch parameters modified")]
public static class DebugInfo {
    private const TimeDisplayMode TimeDisplay = TimeDisplayMode.Time;

    // Flip to false to drop the running-coroutines section from the info panel.
    private const bool ShowCoroutines = true;

    /// Per-FSM transition chain that happened this TAS frame, keyed by Fsm. Built by RecordFsmTransition during
    /// the frame, rendered one line per FSM in GetInfoText, cleared each frame in BeforeTasFrame — so the FSM
    /// section keeps a stable line count while showing what actually fired this frame.
    private static readonly Dictionary<Fsm, List<(string From, string Event, string To)>> fsmTransitions = new();

    /// Record every FSM transition while a TAS runs (cheap, bounded: cleared each frame). Generic over all FSMs;
    /// GetInfoText only renders the ones it lists.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Fsm), "DoTransition")]
    private static void RecordFsmTransition(Fsm __instance, FsmTransition transition) {
        if (!Manager.Running) {
            return;
        }

        if (!fsmTransitions.TryGetValue(__instance, out var list)) {
            fsmTransitions[__instance] = list = [];
        }

        // ActiveStateName here is still the from-state (prefix, before the switch).
        list.Add((__instance.ActiveStateName, transition.EventName, transition.ToState));
    }

    /// The from→event→to chain an FSM went through this frame, e.g. "Dashed --VAULT--> Dash To Vault --CANCEL-->
    /// No Sprint". Empty when nothing fired. Prefixed with a separator so it appends cleanly after the state.
    private static string FsmStateAndActivity(Fsm fsm) {
        if (!fsmTransitions.TryGetValue(fsm, out var list) || list.Count == 0) {
            return fsm.ActiveStateName;
        }

        // The chain already encodes from→…→to (it ends at the state the FSM is heading to), so show it on its own —
        // no separate leading state. fsm.ActiveStateName would still read the pre-switch state on this frame anyway
        // (PlayMaker applies the switch + OnEnter on the next FSM update).
        var chain = list[0].From;
        foreach (var (_, evt, to) in list) {
            chain += $" --{evt}--> {to}";
        }

        return chain;
    }

    [Flags]
    public enum DebugFilter {
        Base = 0,
        All = Base,
    }

    private static string[] states = typeof(HeroControllerStates).GetFields().Select(x => x.Name).ToArray();

    internal static string Vec(Vector2 value, int decimals) {
        string format = $"F{decimals}";
        return $"({value.x.ToString(format)}, {value.y.ToString(format)})";
    }
    internal static string Vec(Vector3 value, int decimals) {
        string format = $"F{decimals}";
        return $"({value.x.ToString(format)}, {value.y.ToString(format)}, {value.z.ToString(format)})";
    }

    public static string GetInfoText(DebugFilter filter = DebugFilter.Base) {
        using var _ = TasTracer.SuppressTrace();
        
        var player = HeroController.SilentInstance;
        if (!player) {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        var rb = player.GetFieldValue<Rigidbody2D>("rb2d")!;

        var text = "";

        // Variable-line-count sections (Enemies, Coroutines) go first so the rest of the panel keeps a stable line
        // layout from the top instead of being pushed around as enemies/coroutines come and go.
        // text += EnemyInfo();
        text += CoroutineInfo();

        text += $"Pos:   {Vec(player.transform.position, TasSettings.PositionDecimals)}\n";
        text += $"Vel:   {Vec(player.current_velocity, TasSettings.SpeedDecimals)}";
        if (rb.linearVelocity != player.current_velocity) text += $" rb {Vec(rb.linearVelocity, TasSettings.SpeedDecimals)}";
        text += "\n";
        var transition = player.transitionState != HeroTransitionState.WAITING_TO_TRANSITION ? $" {player.transitionState}" : "";
        text += $"State: {player.hero_state}{transition} {AnimBody(player.AnimCtrl?.animator, includeSprite: false)}\n";
        // text += $"HP:    {playerData.health} SP: {playerData.silk}\n";


        List<(bool, string)> flags = [
            .. states
                .Where(state => !new[] { "facingRight", "altAttack", "isPaused" }.Contains(state))
                .Select(stateName => (player.cState.GetState(stateName), stateName)),
            (!player.acceptingInput, "!AcceptingInput"),
            (player.GetFieldValue<bool>("dashQueuing"), "DashQueueing"),
            (player.exitedQuake, "ExitedQuake")
        ];

        List<(float, string)> timers = [
            (player.GetFieldValue<float>("attack_time"), "Attack"),
            (player.GetFieldValue<float>("attack_cooldown"), "AttackCD"),
            (player.GetFieldValue<float>("dash_timer"), "Dash"),
            // (player.GetFieldValue<float>("dash_time"), "DashTime"),
            (player.GetFieldValue<float>("dashCooldownTimer"), "DashCD"),
            (player.GetFieldValue<float>("dashLandingTimer"), "DashLanding"),
            (player.GetFieldValue<float>("shadowDashTimer"), "ShadowDash"),
            (player.GetFieldValue<float>("harpoonDashCooldown"), "HarpoonDashCD"),
            (player.GetFieldValue<float>("wandererDashComboWindowTimer"), "WandererDashCombo"),
            (player.GetFieldValue<float>("lookDelayTimer"), "LookDelay"),
            (player.GetFieldValue<float>("bounceTimer"), "Bounce"),
            // (player.GetFieldValue<float>("fallTimer"), "Fall"),
            (player.GetFieldValue<float>("hardLandingTimer"), "HardLanding"),
            (player.GetFieldValue<float>("hardLandFailSafeTimer"), "HardLandFailSafe"),
            (player.GetFieldValue<float>("preventSoftLandTimer"), "PreventSoftLand"),
            (player.GetFieldValue<float>("recoilTimer"), "RecoilH"),
            (player.GetFieldValue<float>("nailChargeTimer"), "NailCharge"),
            (player.GetFieldValue<float>("wallslideClipTimer"), "WallslideClip"),
            (player.GetFieldValue<float>("wallStickTimer"), "WallStick"),
            (player.GetFieldValue<float>("wallClingCooldownTimer"), "WallClingCD"),
            (player.GetFieldValue<float>("hazardDeathTimer"), "HazardDeath"),
            (player.GetFieldValue<float>("floatingBufferTimer"), "FloatingBuffer"),
            (player.GetFieldValue<float>("parryInvulnTimer"), "ParryInvuln"),
            (player.GetFieldValue<float>("revengeWindowTimer"), "RevengeWindow"),
            (player.cState.downSpikeAntic || player.cState.downSpiking ? player.GetFieldValue<float>("downSpikeTimer") : 0, "DownSpike"),
            (player.cState.downSpikeRecovery ? player.GetFieldValue<float>("downSpikeRecoveryTimer") : 0, "DownSpikeRecovery"),
            (player.GetFieldValue<float>("throwToolCooldown"), "ThrowToolCD"),
            (player.GetFieldValue<float>("frostDamageTimer"), "FrostDamage"),
            (player.GetFieldValue<float>("maggotCharmTimer"), "MaggotCharm"),
            (player.GetFieldValue<float>("maxSilkRegenTimer"), "MaxSilkRegen"),
            (player.GetFieldValue<float>("preventCastByDialogueEndTimer"), "PreventCastDialogue"),
            (player.GetFieldValue<float>("shuttlecockTimeResetTimer"), "ShuttlecockReset"),
        ];
        var jump_steps = player.GetFieldValue<int>("jump_steps");
        var jumped_steps = player.cState.jumping || player.cState.doubleJumping ? player.GetFieldValue<int>("jumped_steps") : 0;

        List<(object?, string)> steps = [
            (jump_steps, "JumpSteps"),
            // (jump_steps != jumped_steps ? jumped_steps : null, "JumpedSteps"), // only diverges for edge cases
            (jumped_steps, "JumpedSteps"), // only diverges for edge cases
            QueueSteps("jumpQueueSteps", "JUMP_QUEUE_STEPS", "JumpQueueSteps"),
            (player.GetFieldValue<int>("jumpReleaseQueueSteps"), "JumpReleaseQueueSteps"),
            (player.GetFieldValue<int>("doubleJump_steps"), "DoubleJumpSteps"),
            QueueSteps("doubleJumpQueueSteps", "DOUBLE_JUMP_QUEUE_STEPS", "DoubleJumpQueueSteps"),
            (player.GetFieldValue<int>("landingBufferSteps"), "LandingBufferSteps"),
            (player.GetFieldValue<int>("ledgeBufferSteps"), "LedgeBufferSteps"),
            (player.GetFieldValue<int>("headBumpSteps"), "HeadBumpSteps"),
            (player.GetFieldValue<int>("wallLockSteps"), "WallLockSteps"),
            (player.GetFieldValue<int>("wallUnstickSteps"), "WallUnstickSteps"),
            QueueSteps("dashQueueSteps", "DASH_QUEUE_STEPS", "DashQueueSteps"),
            QueueSteps("attackQueueSteps", "ATTACK_QUEUE_STEPS", "AttackQueueSteps"),
            (player.GetFieldValue<int>("sprintBufferSteps"), "SprintBufferSteps"),
            QueueSteps("harpoonQueueSteps", "HARPOON_QUEUE_STEPS", "HarpoonQueueSteps"),
            QueueSteps("toolThrowQueueSteps", "TOOLTHROW_QUEUE_STEPS", "ToolThrowQueueSteps"),
            (player.GetFieldValue<int>("downspike_rebound_steps"), "DownspikeReboundSteps"),
            (player.GetFieldValue<int>("shuttleCockJumpSteps"), "ShuttlecockJumpSteps"),
        ];

        // The sprint dash-stab is FSM-driven and gated by the sprint FSM's own "Attack Cooldown" var, independent of
        // HeroController.CanAttack (whose attack_cooldown does not apply mid-sprint). Surface it so "Attack" shows
        // during a sprint too. Lean permissive: while sprinting, treat cooldown<=0 as ready.
        bool CanSprintAttack() {
            if (!player.cState.isSprinting || !player.sprintFSM) {
                return false;
            }
            var cd = player.sprintFSM.FsmVariables.GetFsmFloat("Attack Cooldown");
            return cd != null && cd.Value <= 0f;
        }
        List<(bool, string)> can = [
            (Can("CanAttack") || CanSprintAttack(), "Attack"),
            (Can("CanJump"), "Jump"),
            (CanWithoutQueueingInterrupt(() => player.CanDoubleJump()), "DoubleJump"),
            (Can("CanDash"), "Dash"),
            (Can("CanWallJump", true), "WallJump"),
            (Can("CanHarpoonDash"), "HarpoonDash"),
            (Can("CanSuperJump"), "SuperJump"),
            (Can("CanThrowTool", false), "ThrowTool"),
            (Can("CanNailCharge"), "NailCharge"),
            (Can("CanCast"), "Cast"),
            (Can("CanBind"), "Bind"),
            (CanWithoutQueueingInterrupt(() => player.InvokeMethod<bool>("CanFloat", true)), "Float"),
        ];

        text += "Can: " + Flags(can) + "\n";
        text += "Flags: " + Flags(flags) + "\n";
        text += "Timers: " + Timers(timers) + "\n";
        text += "Steps: " + Values(steps) + "\n";

        var currentScene = GameManager._instance?.sceneName;

        PlayMakerFSM?[] fsms = [
            HeroController.instance.sprintFSM,
            HeroController.instance.crestAttacksFSM,
            // HeroController.instance.mantleFSM,
            // Scene FSM (may be absent in a given scene) — the "Control" FSM on the "Dancer Control" object.
            PlayMakerFSM.FsmList.FirstOrDefault(f => f && f.gameObject.name == "Dancer Control" && f.FsmName == "Control"),
        ];

        foreach (var fsm in fsms) {
            if (!fsm) continue;
            text += $"FSM {fsm!.Fsm.Name}: {FsmStateAndActivity(fsm.Fsm)}\n";
        }

        if (player.cState.recoilingLeft || player.cState.recoilingRight) {
            text += $"Recoil: {(player.cState.recoilingRight ? "right" : "left")} " +
                    $"steps={player.GetFieldValue<int>("recoilStepsLeft")} v={player.GetFieldValue<float>("recoilVelocity"):0.##}\n";
        }

        if (player.GetComponent<HeroWaterController>() is { } water && water.CurrentState != HeroWaterController.States.Inactive) {
            var flow = water.GetFieldValue<float>("waterFlowSpeed");
            text += $"Water: {water.CurrentState}{(Mathf.Abs(flow) > 0.01f ? $" flow={flow:0.##}" : "")}\n";
        }

        // Additively loaded scenes besides the gameplay scene (respawn preloaders, persistent scene, …). Surfaced
        // because savestate save/load reacts to them.
        var additiveScenes = new List<string>();
        for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.name != currentScene) {
                additiveScenes.Add(scene.name);
            }
        }

        var sceneLabel = additiveScenes.Count > 0 ? $"{currentScene} +{string.Join(",", additiveScenes)}" : currentScene;

        if (Manager.Running) {
            text += $"[{sceneLabel}] phy={lastPhy} dt={lastDt}\n";
        } else {
            text += $"[{sceneLabel}] phy={lastPhy}\n";
        }

        string customInfo = InfoCustom.GetInfo();
        if (!string.IsNullOrWhiteSpace(customInfo)) {
            text += customInfo + "\n";
        }

        return text;

        // CanDoubleJump/CanFloat set queuedWallJumpInterrupt as a side effect (TryQueueWallJumpInterrupt), which
        // makes the game wall-jump from move input alone. Observe without mutating.
        bool CanWithoutQueueingInterrupt(Func<bool> can) {
            bool queued = player.GetFieldValue<bool>("queuedWallJumpInterrupt");
            var wallObj = player.GetFieldValue<GameObject>("touchingWallObj");
            try {
                return can();
            } finally {
                player.SetFieldValue("queuedWallJumpInterrupt", queued);
                player.SetFieldValue("touchingWallObj", wallObj);
            }
        }

        // Computed "what can I do right now" predicates — the HeroController's own gates, so you get the answer
        // without tracing which cooldown/state blocks an action.
        bool Can(string name, params object?[] args) => player.InvokeMethod<bool>(name, args);

        (object?, string) QueueSteps(string field, string thresholdField, string label) {
            var stepsField = player.GetFieldValue<int>(field);
            var threshold = player.GetFieldValue<int>(thresholdField);
            return (stepsField <= threshold ? stepsField : null, label);
        }
    }

    /// The animation on screen: clip name (quoted) + its frame index within the clip when it maps to one. With
    /// includeSprite, also appends the sprite definition the animator currently has set (ground truth, not derived
    /// from clipTime) in brackets — kept for enemies/bosses, dropped on the hero line for compactness.
    /// Returns just the body (no "Anim:" label) so callers can inline it after other fields.
    private static string AnimBody(tk2dSpriteAnimator? anim, bool includeSprite = true) {
        if (anim == null || anim.Sprite is not { } sprite) {
            return "-";
        }

        var clip = anim.CurrentClip;
        var name = sprite.CurrentSprite?.name ?? "?";
        var frame = "";
        if (clip?.frames is { } frames) {
            var idx = Array.FindIndex(frames, fr => fr.spriteId == sprite.spriteId);
            if (idx >= 0) {
                // 1-based for display ("frame N of M" reads naturally); tk2d indexes frames[] 0-based internally.
                frame = $" {idx + 1}/{frames.Length}";
            }
        }

        return $"'{(clip != null ? clip.name : "-")}'{frame}{(includeSprite ? $" [{name}]" : "")}";
    }

    // Boss (enemy) position + animation, read from the game's own static list of active HealthManagers — cheap
    // (no per-frame FindObjectsByType; only a handful live during a fight), reusing the hero's AnimInfo formatter.
    private static string EnemyInfo() {
        if (typeof(HealthManager).GetFieldValue<List<HealthManager>>("_activeHealthManagers") is not { } bosses) {
            return "";
        }

        var text = "";
        foreach (var hm in bosses) {
            if (!hm) {
                continue;
            }

            var fsm = hm.GetComponent<PlayMakerFSM>();
            text += $"  {hm.name} hp={hm.hp} {hm.transform.position}: '{FsmStateAndActivity(fsm.Fsm)}' {AnimBody(hm.GetFieldValue<tk2dSpriteAnimator>("animator"), false)}{ITweenInfo(hm.gameObject)}\n";
        }

        return text == "" ? "" : "Enemies:\n" + text;
    }

    private static string ITweenInfo(GameObject go) {
        var parts = new List<string>();
        foreach (var t in go.GetComponents<iTween>()) {
            if (!t.isRunning) {
                continue;
            }

            var pct = t.GetFieldValue<float>("percentage");
            var target = t.GetFieldValue<Vector3[]>("vector3s") is { Length: > 1 } v ? Vec(v[1], 1) : "?";
            parts.Add($"{t.type}/{t.method} {pct * 100:0}% →{target} t={t.time * (1 - pct):0.00}/{t.time:0.00}");
        }

        return parts.Count == 0 ? "" : " tween[" + string.Join(" ", parts) + "]";
    }

    private static string CoroutineInfo() {
        if (!ShowCoroutines) {
            return "";
        }

        var coroutines = CoroutineTracker.RunningDescriptions();
        if (coroutines.Count == 0) {
            return "";
        }

        var grouped = coroutines
            .GroupBy(desc => desc)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key);
        return "Coroutines: " + string.Join(",\n", grouped) + "\n";
    }

    private static string Flags(IEnumerable<(bool, string)> flags) {
        var text = "";
        foreach (var (val, name) in flags) {
            if (!val) continue;
            text += $"{name} ";
        }

        return text;
    }

    private static string Values(IEnumerable<(object?, string)> values) {
        var text = "";
        foreach (var (val, name) in values) {
            if (val is null or "" or 0) continue;
            text += $"{name}={val} ";
        }

        return text;
    }

    private static string Timers(IEnumerable<(float, string)> timers) {
        var text = "";
        foreach (var (timer, name) in timers) {
            if (timer <= 0) continue;
            text += $"{name}({FormatTime(timer)})";
            text += " ";
        }

        return text;
    }

    private static string RoundUpTimeToFrames(float time) {
        if (float.IsInfinity(time)) {
            return "Inf";
        }

        var frames = time * InputHelper.CurrentTasFramerate;
        var rounded = Math.Round(frames, 4);
        return ((int)Math.Ceiling(rounded)).ToString();
    }

    private enum TimeDisplayMode {
        Frames,
        Time,
    }

    private static string FormatTime(float time) =>
        TimeDisplay == TimeDisplayMode.Time ? $"{time:0.000}" : RoundUpTimeToFrames(time);

    private static string FormatTimeMaybe(float time) => time > 0 ? $"{FormatTime(time)} " : "";

    private static float lastDt = 0;
    private static int lastPhy = 0;
    private static int fixedUpdateCounter = 0;

    internal static void FixedUpdate() {
        fixedUpdateCounter++;
    }

    internal static void PostLateUpdate() {
        // Snapshot only on advancing frames; a paused TAS keeps ticking this system at timeScale=0 and would
        // otherwise overwrite the last advanced frame's dt/phy with zeros.
        if (!Manager.Running || Manager.CurrState != Manager.State.Paused) {
            lastDt = Time.deltaTime;
            lastPhy = fixedUpdateCounter;
        }

        fixedUpdateCounter = 0;
    }

    [BeforeActiveTasFrame]
    private static void BeforeTasFrame() {
        fsmTransitions.Clear();
    }

}
