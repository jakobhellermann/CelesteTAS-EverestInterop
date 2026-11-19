using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TAS.EverestInterop.Hitboxes;

[Flags]
internal enum RaycastFilter {
    All = Raycast | Linecast | Boxcast,
    Raycast = 1 << 0,
    Linecast = 1 << 2,
    Boxcast = 1 << 3,
}

[HarmonyPatch]
public static class RaycastDrawing {
    private static bool ShowRaycasts => TasSettings.ShowRaycasts.Value;
    
    private const RaycastFilter Filter = RaycastFilter.All;
    private static readonly Color ColorRaycast = Color.yellow;
    private static readonly Color ColorLinecast = Color.cyan;
    private static readonly Color ColorBoxcast = Color.magenta;

    private static readonly List<(Vector2 origin, Vector2 direction, float distance)> raycasts = [];
    private static readonly List<(Vector2 start, Vector2 end)> linecasts = [];
    private static readonly List<(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance)> boxcasts = [];

    /// Casts are kept until the next frame that actually plays, so a paused TAS keeps showing its last frame's casts
    private static int recordedFrame = -1;

    private static bool BeginRecord() {
        if (Manager.Running && Manager.CurrState == Manager.State.Paused) return false;

        if (Time.frameCount != recordedFrame) {
            recordedFrame = Time.frameCount;
            raycasts.Clear();
            linecasts.Clear();
            boxcasts.Clear();
        }
        return true;
    }

    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CapsuleCast_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CapsuleCastArray_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CircleCast_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CircleCastArray_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CapsuleCastArray_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CapsuleCastArray_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.OverlapBox_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.OverlapCapsule_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.OverlapCircle_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.OverlapPoint_Internal))]
    [HarmonyPostfix]
    private static void Todo(MethodBase __originalMethod) {
        if (loggedTodos.Add(__originalMethod.Name)) {
            Log.Info($"Todo cast {__originalMethod.Name}");
        }
    }

    private static readonly HashSet<string> loggedTodos = [];

    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.Raycast_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.RaycastArray_Internal))]
    [HarmonyPostfix]
    private static void Raycast(
        PhysicsScene2D physicsScene,
        Vector2 origin,
        Vector2 direction,
        float distance,
        ContactFilter2D contactFilter
    ) {
        if (!Filter.HasFlag(RaycastFilter.Raycast) || !ShowRaycasts || !BeginRecord()) return;

        raycasts.Add((origin, direction, distance));
    }

    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.Linecast_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.LinecastArray_Internal))]
    [HarmonyPostfix]
    private static void Linecast(
        PhysicsScene2D physicsScene,
        Vector2 start,
        Vector2 end,
        ContactFilter2D contactFilter
    ) {
        if (!Filter.HasFlag(RaycastFilter.Linecast) || !ShowRaycasts || !BeginRecord()) return;

        linecasts.Add((start, end));
    }

    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.BoxCast_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.BoxCastArray_Internal))]
    [HarmonyPostfix]
    private static void BoxCast(
        PhysicsScene2D physicsScene,
        Vector2 origin,
        Vector2 size,
        float angle,
        Vector2 direction,
        float distance,
        ContactFilter2D contactFilter
    ) {
        if (!Filter.HasFlag(RaycastFilter.Boxcast) || !ShowRaycasts || !BeginRecord()) return;

        boxcasts.Add((origin, size, angle, direction, distance));
    }

    public static void OnGUI() {
        if (!ShowRaycasts) {
            boxcasts.Clear();
            raycasts.Clear();
            linecasts.Clear();
            return;
        }

        // Other IMGUI events use a different projection and would draw a shifted, y-flipped copy
        if (Event.current?.type != EventType.Repaint) return;
        if (GameInterop.MainCamera is not { } camera) return;

        // Drops the previous frame's casts when this played frame made none
        BeginRecord();

        foreach (var (origin, direction, distance) in raycasts) {
            DrawLineWorld(camera, origin, origin + (direction * distance), ColorRaycast);
        }

        foreach (var (from, to) in linecasts) {
            DrawLineWorld(camera, from, to, ColorLinecast);
        }

        foreach (var (origin, size, angle, direction, distance) in boxcasts) {
            if (angle != 0) {
                Log.Info($"Unhandled: Box Raycast with angle {angle}");
            }

            var target = origin + (direction * distance);
            var half = origin + ((target - origin) / 2);
            DrawBoxWorld(camera, half, new Vector2(distance + size.x, size.y), ColorBoxcast);
        }
    }

    private const float LineWidth = 1f;
    private const bool AntiAlias = true;

    private static void DrawLineWorld(Camera camera, Vector2 from, Vector2 to, Color color) {
        Drawing.DrawLine(WorldToScreenPoint(camera, from),
            WorldToScreenPoint(camera, to),
            color,
            LineWidth,
            AntiAlias);
    }

    private static void DrawPointSequenceWorld(Camera camera, IList<Vector2> points, Color color) {
        for (var i = 0; i < points.Count - 1; i++) {
            var from = WorldToScreenPoint(camera, points[i]);
            var to = WorldToScreenPoint(camera, points[i + 1]);
            Drawing.DrawLine(from, to, color, LineWidth, AntiAlias);
        }
    }

    private static void DrawBoxWorld(Camera camera, Vector2 origin, Vector2 size, Color color) {
        var halfSize = size / 2f;
        var topLeft = origin + new Vector2(-halfSize.x, halfSize.y);
        var topRight = origin + halfSize;
        var bottomRight = origin + new Vector2(halfSize.x, -halfSize.y);
        var bottomLeft = origin - halfSize;
        DrawPointSequenceWorld(camera, [topLeft, topRight, bottomRight, bottomLeft, topLeft], color);
    }

    private static Vector2 WorldToScreenPoint(Camera camera, Vector2 point) {
        Vector2 result = camera.WorldToScreenPoint(point);
        return new Vector2((int)Math.Round(result.x), (int)Math.Round(Screen.height - result.y));
    }
}
