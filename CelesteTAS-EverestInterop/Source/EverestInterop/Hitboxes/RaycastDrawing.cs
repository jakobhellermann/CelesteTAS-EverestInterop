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
    private const RaycastFilter Filter = RaycastFilter.All;
    private const bool KeepOnlyLast = true;
    private static readonly Color ColorRaycast = Color.yellow;
    private static readonly Color ColorLinecast = Color.cyan;
    private static readonly Color ColorBoxcast = Color.magenta;

    private static List<(Vector2 origin, Vector2 direction, float distance, int frame, float time)> raycasts = [];
    private static List<(Vector2 origin, Vector2 direction, int frame, float time)> linecasts = [];

    private static
        List<(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, int frame, float time)>
        boxcasts = [];

    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CapsuleCast_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CircleCast_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CircleCast_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.RaycastArray_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CapsuleCastArray_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CircleCastArray_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.CircleCastArray_Internal))]
    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.LinecastArray_Internal))]
    [HarmonyPostfix]
    private static void Todo(MethodBase __originalMethod) {
        Log.Info($"Todo cast {__originalMethod.Name}");
    }

    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.Raycast_Internal))]
    [HarmonyPostfix]
    private static void Raycast(
        PhysicsScene2D physicsScene,
        Vector2 origin,
        Vector2 direction,
        float distance,
        ContactFilter2D contactFilter
    ) {
        if (!Filter.HasFlag(RaycastFilter.Raycast)) return;

        raycasts.Add((origin, direction, distance, Time.frameCount, Time.time));
    }

    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.Linecast_Internal))]
    [HarmonyPostfix]
    private static void Linecast(
        PhysicsScene2D physicsScene,
        Vector2 start,
        Vector2 end,
        ContactFilter2D contactFilter
    ) {
        if (!Filter.HasFlag(RaycastFilter.Linecast)) return;

        linecasts.Add((start, end, Time.frameCount, Time.time));
    }

    [HarmonyPatch(typeof(PhysicsScene2D), nameof(PhysicsScene2D.BoxCast_Internal))]
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
        if (!Filter.HasFlag(RaycastFilter.Boxcast)) return;

        boxcasts.Add((origin, size, angle, direction, distance, Time.frameCount, Time.time));
    }

    public static void OnGUI() {
        if (GameInterop.MainCamera is not { } camera) return;

        foreach (var (origin, direction, distance, _, _) in raycasts) {
            DrawLineWorld(camera, origin, origin + (direction * distance), ColorRaycast);
        }

        foreach (var (from, to, _, _) in linecasts) {
            DrawLineWorld(camera, from, to, ColorLinecast);
        }

        foreach (var (origin, size, angle, direction, distance, _, _) in boxcasts) {
            if (angle != 0) {
                Log.Info($"Unhandled: Box Raycast with angle {angle}");
            }

            var target = origin + (direction * distance);
            var half = origin + ((target - origin) / 2);
            DrawBoxWorld(camera, half, new Vector2(distance + size.x, size.y), ColorBoxcast);
        }

        if (Manager.Running && Manager.CurrState is Manager.State.Running or Manager.State.FrameAdvance) {
            raycasts.Clear();
            boxcasts.Clear();
        }

        const float threshold = 1;
        if (KeepOnlyLast) {
            if (boxcasts.Count > 0) boxcasts.RemoveAll(tuple => !Mathf.Approximately(tuple.time, boxcasts.Last().time));
            if (raycasts.Count > 0) raycasts.RemoveAll(tuple => !Mathf.Approximately(tuple.time, raycasts.Last().time));
            if (linecasts.Count > 0)
                linecasts.RemoveAll(tuple => !Mathf.Approximately(tuple.time, linecasts.Last().time));
        }

        var removeOlderThan = Time.time - threshold;
        boxcasts.RemoveAll(tuple => tuple.time < removeOlderThan);
        raycasts.RemoveAll(tuple => tuple.time < removeOlderThan);
        linecasts.RemoveAll(tuple => tuple.time < removeOlderThan);
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
