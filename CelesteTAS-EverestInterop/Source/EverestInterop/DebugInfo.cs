using GlobalEnums;
using System;
using System.Collections.Generic;
using System.Linq;
using TAS.Utils;
using UnityEngine;

namespace TAS;

public static class DebugInfo {
    private const TimeDisplayMode TimeDisplay = TimeDisplayMode.Frames;

    [Flags]
    public enum DebugFilter {
        Base = 0,
        All = Base,
    }

    private static string[] states = typeof(HeroControllerStates).GetFields().Select(x => x.Name).ToArray();

    private static string Vec(Vector2 value, int decimals) {
        string format = $"F{decimals}";
        return $"({value.x.ToString(format)}, {value.y.ToString(format)})";
    }
    private static string Vec(Vector3 value, int decimals) {
        string format = $"F{decimals}";
        return $"({value.x.ToString(format)}, {value.y.ToString(format)}, {value.z.ToString(format)})";
    }

    public static string GetInfoText(DebugFilter filter = DebugFilter.Base) {
        var player = HeroController.SilentInstance;
        if (!player) {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        var rb = player.GetFieldValue<Rigidbody2D>("rb2d")!;
        var playerData = PlayerData.instance;

        var text = "";
        text += $"Pos:   {Vec(player.transform.position, TasSettings.PositionDecimals)}\n";
        text += $"Vel:   {Vec(player.current_velocity, TasSettings.SpeedDecimals)}";
        if (rb.velocity != player.current_velocity) text += $" rb {Vec(rb.velocity, TasSettings.SpeedDecimals)}";
        text += "\n";
        text +=
            $"State: {player.hero_state} {(player.transitionState != HeroTransitionState.WAITING_TO_TRANSITION ? player.transitionState : "")}\n";
        text += $"HP:    {playerData.health}\n";
        text += $"MP:    {playerData.MPCharge + playerData.MPReserve}\n";


        List<(bool, string)> flags = [];
        flags.AddRange(states.Select(stateName => (player.cState.GetState(stateName), stateName)));
        flags.AddRange([
            (player.GetFieldValue<bool>("dashQueuing"), "DashQueueing"),
            (player.exitedQuake, "ExitedQuake"),
        ]);

        List<(float, string)> timers = [
            (player.GetFieldValue<float>("dash_timer"), "Dash"),
            (player.GetFieldValue<float>("back_dash_timer"), "BackDash"),
            (player.GetFieldValue<float>("shadow_dash_timer"), "ShadowDash"),
            (player.GetFieldValue<float>("attack_time"), "Attack"),
            (player.GetFieldValue<float>("attack_time"), "Attack"),
            // (Time.timeSinceLevelLoad -  player.GetFieldValue<float>("altAttackTime"), "AltAttack"),
            (player.GetFieldValue<float>("attack_cooldown"), "AttackCD"),
            (player.GetFieldValue<float>("dash_timer"), "DashTimer"),
            (player.GetFieldValue<float>("lookDelayTimer"), "LookDelay"),
            (player.GetFieldValue<float>("bounceTimer"), "Bounce"),
            (player.GetFieldValue<float>("recoilHorizontalTimer"), "RecoilH"),
            (player.GetFieldValue<float>("runPuffTimer"), "RunPuff"),
            (player.GetFieldValue<float>("hardLandingTimer"), "HardLanding"),
            (player.GetFieldValue<float>("dashLandingTimer"), "DashLanding"),
            (player.GetFieldValue<float>("recoilTimer"), "RecoilH"),
            (player.GetFieldValue<float>("nailChargeTimer"), "NailCharge"),
            (player.GetFieldValue<float>("wallslideClipTimer"), "NailCharge"),
            (player.GetFieldValue<float>("hardLandFailSafeTimer"), "HardLandFailSafe"),
            (player.GetFieldValue<float>("hazardDeathTimer"), "HazardDeath"),
            (player.GetFieldValue<float>("floatingBufferTimer"), "FloatingBuffer"),
            (player.GetFieldValue<float>("parryInvulnTimer"), "ParryInvuln"),
        ];
        List<(object?, string)> values = [
            (player.GetFieldValue<int>("jump_steps"), "JumpSteps"),
            (player.GetFieldValue<int>("jumped_steps"), "JumpedSteps"),
            (player.GetFieldValue<int>("doubleJump_steps"), "DoubleJumpSteps"),
            (player.GetFieldValue<int>("recoilSteps"), "RecoilSteps"),
            (player.GetFieldValue<int>("landingBufferSteps"), "LandingBufferSteps"),
            (player.GetFieldValue<int>("wallLockSteps"), "WallLockSteps"),
            (player.GetFieldValue<int>("dashQueueSteps"), "DashQueueSteps"),
            (player.GetFieldValue<int>("wallUnstickSteps"), "WallUnstickSteps"),
            (player.GetFieldValue<int>("ledgeBufferSteps"), "LedgeBufferSteps"),
            (player.GetFieldValue<int>("headBumpSteps"), "HeadBumpSteps"),
        ];
        text += "Flags: " + Flags(flags) + "\n";
        text += "Timers: " + Timers(timers) + "\n";
        text += "Values: " + Values(values) + "\n";


        return text;
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
}
