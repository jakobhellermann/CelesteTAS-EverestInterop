using System;
using System.Collections.Generic;
using TAS.Module;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TAS.EverestInterop.Hitboxes;

[Flags]
// ReSharper disable InconsistentNaming
public enum HitboxType : uint {
    All = uint.MaxValue,
    Default = LEVEL | ENEMY | PLAYER,

    LEVEL = Terrain | ChangeSceneTrigger,
    Terrain = 1 << 1,
    ChangeSceneTrigger = 1 << 2,

    ENEMY = Enemy,
    Enemy = 1 << 6,

    PLAYER = Player,
    Player = 1 << 10,

    Uncategorized = 1u << 31,
}
// ReSharper restore InconsistentNaming

public class HitboxModule : MonoBehaviour {
    private static bool HitboxesVisible => TasSettings.ShowHitboxes.Value;
    private static HitboxType Filter => TasSettings.HitboxFilter.Value;

    private void Awake() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoad;
        colliders = new SortedDictionary<HitboxType, HashSet<Collider2D>>();

        if (HitboxesVisible) Reload();
    }

    private void OnDestroy() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoad;
    }

    private void OnSceneLoad(Scene scene, LoadSceneMode mode) => Reload();

    #region Drawing

    private static Color GetColor(HitboxType type) {
        if (hitboxColors.TryGetValue(type, out var color)) return color;

        var hash = type.GetHashCode();

        float hue = ((hash % 360) + 360) % 360;
        const float lightness = 0.8f;
        const float chroma = 0.2f;
        var n = hitboxColors[type] = ColorUtil.OklchToColor(lightness, chroma, hue);
        return n;
    }

    private static Dictionary<HitboxType, Color> hitboxColors = new() {
        /*{ HitboxType.PlayerItemPicker, new Color(0.8f, 0.4f, 0) },
        { HitboxType.PlayerHealth, new Color(0.4f, 0.8f, 0) },
        { HitboxType.DamageDealer, new Color(0.8f, 0, 0) },
        { HitboxType.PathFindAgent, Color.cyan },
        { HitboxType.Terrain, new Color(0, 0.8f, 0) },
        { HitboxType.Trigger, new Color(0.5f, 0.5f, 1f) },
        { HitboxType.EffectReceiver, new Color(1f, 0.75f, 0.8f) },
        { HitboxType.Trap, new Color(0.0f, 0.0f, 0.5f) },
        { HitboxType.AttackSensor, new Color(0.5f, 0.2f, 0.5f) },
        { HitboxType.Audio, new Color(0.8f, 0.8f, 0.5f) },
        { HitboxType.MonsterPushAway, new Color(0.9f, 0.3f, 0.4f) },
        { HitboxType.PathFindTarget, new Color(0.3f, 0.6f, 0.8f) },
        { HitboxType.EffectDealer, new Color(0.5f, 0.15f, 0.8f) },
        { HitboxType.PlayerSensor, new Color(0.5f, 0.95f, 0.3f) },
        { HitboxType.Player, new Color(0.5f, 0.95f, 0.3f) },
        { HitboxType.ActorSensor, new Color(0.1f, 0.95f, 0.3f) },
        { HitboxType.GeneralFindable, new Color(0.8f, 0.15f, 0.3f) },
        { HitboxType.ChangeSceneTrigger, new Color(0.8f, 0.85f, 0.3f) },
        { HitboxType.FinderGeneral, new Color(0.8f, 0.85f, 0.8f) },
        { HitboxType.HookRange, new Color(0.3f, 0.85f, 0.8f) },
        { HitboxType.Interactable, new Color(0.8f, 0.25f, 0.8f) },
        { HitboxType.Uncategorized, new Color(0.9f, 0.6f, 0.4f) },
        { HitboxType.ActorBody, new Color(1, 0.5f, 1) },
        { HitboxType.DecreasePosture, new Color(0.4f, 0.5f, 1) },
        { HitboxType.PlayerSpawnPoint, new Color(0.65f, 0.8f, 0.6f) },
        { HitboxType.Monster, new Color(0.65f, 0.3f, 0.6f) },

        { HitboxType.Todo, new Color(1, 0, 1) },*/
    };

    private static Dictionary<HitboxType, int> depths = [];


    private SortedDictionary<HitboxType, HashSet<Collider2D>> colliders = null!;

    private static float LineWidth => Math.Max(0.8f, Screen.width / 2000f);
    private const bool AntiAlias = true;

    public void Reload() {
        colliders.Clear();
        try {
            foreach (var col in Resources.FindObjectsOfTypeAll<Collider2D>()) {
                TryAddHitboxes(col);
            }
        } catch (Exception e) {
            Log.Toast(e.ToString());
        }
    }

    public void UpdateHitbox(GameObject go) {
        foreach (var col in go.GetComponentsInChildren<Collider2D>(true)) TryAddHitboxes(col);
    }

    private void TryAddHitboxes(Collider2D collider2D) {
        if (!collider2D) return;
        if (collider2D is not (BoxCollider2D or PolygonCollider2D or EdgeCollider2D or CircleCollider2D)) return;

        var go = collider2D.gameObject;

        {
            colliders.AddToKey(HitboxType.Uncategorized, collider2D);
        }
    }

    public void OnGUI() {
        RaycastDrawing.OnGUI();
        if (!HitboxesVisible) return;

        try {
            if (Event.current?.type != EventType.Repaint) return;
            if (!GameManager.instance) return;
            if (GameInterop.MainCamera is not { } camera) return;

            GUI.depth = int.MaxValue;

            foreach (var (type, typeColliders) in colliders) {
                if (!Filter.HasFlag(type)) {
                    continue;
                }

                foreach (var collider2D in typeColliders) {
                    if (!collider2D) continue;

                    var onScreen = OnScreen(collider2D, camera);
                    if (onScreen) DrawHitbox(camera, collider2D, type);
                }
            }
        } catch (Exception e) {
            Log.Toast(e);
        }
    }

    private static bool OnScreen(Collider2D collider2D, Camera camera) {
        var bounds = collider2D.bounds;


        var bl = bounds.min;
        var br = new Vector3(bounds.max.x, bounds.min.y);
        var tl = new Vector3(bounds.min.x, bounds.max.y);
        var tr = bounds.max;


        return PointOnScreen(collider2D.transform.position, camera)
               || PointOnScreen(bl, camera)
               || PointOnScreen(br, camera)
               || PointOnScreen(tl, camera)
               || PointOnScreen(tr, camera);

        static bool PointOnScreen(Vector2 point, Camera camera) {
            var screenPosition = camera.WorldToViewportPoint(point);
            return screenPosition.x is > 0 and < 1 && screenPosition.y is > 0 and < 1;
        }
    }

    private static void DrawHitbox(Camera camera, Collider2D collider2D, HitboxType hitboxType) {
        if (!collider2D || !collider2D.isActiveAndEnabled) return;

        var origDepth = GUI.depth;
        GUI.depth = depths.TryGetValue(hitboxType, out var depth) ? depth : (int)hitboxType;
        var color = GetColor(hitboxType);

        switch (collider2D) {
            case BoxCollider2D boxCollider2D:
                DrawBoxLocal(camera, collider2D, boxCollider2D.size, color);
                break;
            case EdgeCollider2D edgeCollider2D:
                DrawPointSequenceLocal(camera, collider2D, edgeCollider2D.points, color);
                break;
            case CircleCollider2D circleCollider2D:
                DrawCircleLocal(camera, circleCollider2D, Vector2.zero, circleCollider2D.radius, color);
                break;
            case PolygonCollider2D polygonCollider2D:
                for (var i = 0; i < polygonCollider2D.pathCount; i++) {
                    List<Vector2> polygonPoints = new(polygonCollider2D.GetPath(i));
                    if (polygonPoints.Count > 0) polygonPoints.Add(polygonPoints[0]);
                    DrawPointSequenceLocal(camera, collider2D, polygonPoints, GetColor(hitboxType));
                }

                break;
        }

        GUI.depth = origDepth;
    }

    private static void DrawPointSequenceLocal(Camera camera, Collider2D collider2D,
        IList<Vector2> points,
        Color color) {
        for (var i = 0; i < points.Count - 1; i++) {
            var pointA = LocalToScreenPoint(camera, collider2D, points[i]);
            var pointB = LocalToScreenPoint(camera, collider2D, points[i + 1]);
            Drawing.DrawLine(pointA, pointB, color, LineWidth, AntiAlias);
        }
    }

    private static void DrawBoxLocal(Camera camera, Collider2D collider2D, Vector2 size, Color color) {
        var halfSize = size / 2f;
        Vector2 topLeft = new(-halfSize.x, halfSize.y);
        var topRight = halfSize;
        Vector2 bottomRight = new(halfSize.x, -halfSize.y);
        var bottomLeft = -halfSize;
        var boxPoints = new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft };
        DrawPointSequenceLocal(camera, collider2D, boxPoints, color);
    }


    private static void DrawCircleLocal(Camera camera, Collider2D collider2D, Vector2 center, float radius,
        Color color) {
        var centerScreen = LocalToScreenPoint(camera, collider2D, center);
        var right = LocalToScreenPoint(camera, collider2D, Vector2.right * radius);
        var radiusScreen = (int)Math.Round(Vector2.Distance(centerScreen, right));
        var segments = Mathf.Clamp(radiusScreen / 16, 4, 32);

        Drawing.DrawCircle(centerScreen,
            radiusScreen,
            color,
            LineWidth,
            AntiAlias,
            segments);
    }


    private static Vector2 WorldToScreenPoint(Camera camera, Vector2 point) {
        Vector2 result = camera.WorldToScreenPoint(point);
        return new Vector2((int)Math.Round(result.x), (int)Math.Round(Screen.height - result.y));
    }

    private static Vector2 LocalToScreenPoint(Camera camera, Collider2D collider2D, Vector2 point) =>
        WorldToScreenPoint(camera, collider2D.transform.TransformPoint(point + collider2D.offset));

    #endregion
}

internal static class CollectionExtensions {
    public static void AddToKey<TKey, TValue>(this IDictionary<TKey, HashSet<TValue>> dict, TKey key,
        TValue value) {
        if (dict.TryGetValue(key, out var list)) {
            list.Add(value);
            return;
        }

        dict[key] = [value];
    }
}
