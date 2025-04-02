using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using TAS;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Events;
using UnityEngine.Tilemaps;
using IStateMachine = MonsterLove.StateMachine.IStateMachine;
using Object = UnityEngine.Object;

namespace Snapshots;

public static class SnapshotSerializer {
    public static JToken Snapshot(object obj) => JToken.FromObject(obj, JsonSerializer.Create(Settings));

    public static string SnapshotToString(object? obj) =>
        JsonConvert.SerializeObject(obj, Formatting.Indented, Settings);

    public static void Populate(object target, string json) {
        using JsonReader reader = new JsonTextReader(new StringReader(json));
        JsonSerializer.Create(Settings).Populate(reader, target);
    }

    public static void Populate(object target, JToken json) {
        var serializer = JsonSerializer.Create(Settings);
        using JsonReader reader = new JTokenReader(json);
        serializer.Populate(reader, target);
    }

    public static readonly JsonConverter[] UnityConverters = [
        new TransformConverter(),
        new Vector2Converter(),
        new Vector3Converter(),
        new Vector4Converter(),
        new QuatConverter(),
        new ColorConverter(),
        new Color32Converter(),
        new AnimatorConverter(),
        new StringEnumConverter(),
    ];


    private static readonly JsonSerializerSettings Settings = new() {
        ReferenceLoopHandling = ReferenceLoopHandling.Error,
        Error = (_, args) => {
            args.ErrorContext.Handled = true;
            Log.Error(
                $"Serialization error while creating snapshot: {args.CurrentObject?.GetType()}: {args.ErrorContext.Path}: {args.ErrorContext.Error.Message}");
        },
        ContractResolver = CustomResolver,
        Converters = [..UnityConverters],
    };

    public static CustomizableContractResolver CustomResolver => new() {
        ContainerTypesToIgnore = [
            typeof(MonoBehaviour),
            typeof(Component),
            typeof(Object),
        ],
        FieldTypesToIgnore = [
            // ignored
            typeof(PoolObject),
            typeof(MonoBehaviour),
            typeof(Bounds),
            typeof(GameObject),
            typeof(UnityEventBase),
            typeof(Action),
            typeof(Delegate),
            typeof(FxPlayer),
            typeof(MappingState.StateEvents),
            typeof(IEffectOwner),
            typeof(PositionConstraint),
            typeof(PathArea),
            typeof(IEffectHitHandler),
            typeof(ICooldownEffectReceiver),
            typeof(PathToAreaFinder),
            // typeof(mixpanel.Value),
            typeof(Sprite),
            typeof(Tilemap),
            typeof(LineRenderer),
            typeof(Color),
            typeof(VelocityModifierParam),
            typeof(ParticleSystem),
            typeof(TestRope.RopeSegment),
            typeof(AnimationCurve),
            typeof(AnimationClip),
            typeof(IActiveOverrider),
            typeof(CullingObserver),
            typeof(Rect),
            typeof(Timer.DelayTask),
            // todo
            typeof(PrimeTween.Tween),
            typeof(Rigidbody2D), // maybe
            typeof(Transform), // maybe
            typeof(SpriteRenderer), // maybe
            typeof(LayerMask), // maybe
            typeof(Collider2D), // maybe
            typeof(AbilityWrapper), // bugs out
            typeof(EffectHitData),
            typeof(IStateMachine),
            typeof(RuntimeConditionVote),
            typeof(ScriptableObject),
            typeof(StatData),
            typeof(CharacterStat),
            typeof(StatModifier),
            typeof(MapIndexReference.MapTileData), // maybe
        ],
        FieldDenylist = new Dictionary<Type, string[]> {
            { typeof(StealthGameMonster), ["boxColliderSizes"] },
            { typeof(FlyingMonster), ["boxColliderSizes"] },
        },
    };
}
