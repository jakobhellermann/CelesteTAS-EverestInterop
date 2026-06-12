using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using TAS.Communication;
using TAS.Utils;
using UnityEngine;

namespace TAS;

/// Tracks which coroutines are currently running so DebugInfo can surface them. Unity exposes no global coroutine
/// list, so StartCoroutine's enumerator is wrapped (see CoroutineTrackerPatch) in a forwarding enumerator that
/// registers on its first MoveNext and unregisters when it completes or throws.
///
/// This matters for determinism: coroutines advance on the render loop via WaitForSeconds, which reads the *real*
/// Time.timeScale (our timeScale override is only a getter patch that fools managed code, not Unity's native
/// scheduler) — so a coroutine can keep ticking through a savestate load in wall-clock time. Seeing what is running
/// during the load window is the first step to catching that.
internal static class CoroutineTracker {
    private static readonly HashSet<TrackedRoutine> running = [];

    // Maps the Coroutine handle StartCoroutine returns back to our wrapper. StopCoroutine(handle) and
    // StopAllCoroutines stop a coroutine without driving another MoveNext, so the wrapper's own Finish never fires —
    // without this a stopped routine lingers in the tracker forever (only a game reload clears it).
    private static readonly Dictionary<Coroutine, TrackedRoutine> byHandle = new();

    // Coroutines known to be purely cosmetic (RNG-isolated separately, see RandomIsolationPatch) — excluded from
    // tracking so the display and the traced Info line show only potentially interesting coroutines. Whack-a-mole by
    // label; add entries as cosmetic churn shows up in the diff.
    private static readonly HashSet<string> Denylist = [
        "MenuSelectable.ValidateDeselect",
        "ShellShard.EnableShineEffect",
        // Cosmetic fade of darkness masks. Its persisted state (hasBeenUncovered → SceneData) is captured and compared
        // separately; the coroutine-count variance we see is just masks mid-fade, so hide it from the display.
        "Remasker.FadeWatch",
        // FadeAlpha is spawned by FadeWatch and only tweens the mask's alpha/color — same cosmetic-fade rationale.
        "Remasker.FadeAlpha",
        // Camera lock-area bounds setup: computes camera framing bounds from a box collider. Camera framing never
        // affects gameplay or physics.
        "CameraLockArea.StartRoutine",
        "ShineAnimSequence.ShineSequence",
        // Camera shake is always a visual effect, never gameplay.
        "CameraShakeOnEnable.ShakeCameraDelayed",
        // Our own DevServer HTTP loop (DevUtils), not game code.
        "HttpServer.RunAsync",
        // Ambient decor glints/drips — cosmetic, always running.
        "ShineObject.ShineAnim",
        "Dripper.Behaviour",
        // Ambient water drip: falls, impacts the ground (OnCollisionEnter2D, no hero contact), plays audio, resets.
        // Draws RNG for its idle interval and impact sound; no gameplay/hero state.
        "WaterDrip.Drip",
        // UI-only coroutines: Unity/TMPro UI tweens (ScrollRect/Toggle color+position), layout rebuilds, scroll
        // animations, the UniverseLib debug-menu cursor unlock, and inventory-crest display transitions. No gameplay
        // state, so hide them from the display.
        "TweenRunner`1.Start",
        "LayoutGroup.DelayedSetDirty",
        "ScrollView.ScrollDistance",
        "CursorUnlocker.UnlockCoroutine",
        "InventoryToolCrest.TransitionDisplayState",
        // Control-remap UI: walks the mappable-button widgets calling ShowCurrentBinding() to draw the current
        // controller glyphs, one per frame. Boot/menu UI display only — shows up on early frames right after a game
        // start (so its presence depends on wall-clock-since-boot), touches no hero/gameplay state.
        "UIButtonSkins.InitialButtonMappingSetup",
        // HUD frame appear animation (e.g. the silk/bind orb HUD re-appearing on scene entry): plays a tk2d sprite
        // clip + a UI audio one-shot. Cosmetic HUD display; its presence at a given frame tracks the transition's
        // (hidden) wall-clock timing, not hero/gameplay state.
        "BindOrbHudFrame.FrameAppear",
        // Camera shake and death-debris chunks tumbling — purely visual.
        "CameraShakeManager.EvaluateShakesTimed",
        "CogRollThenFallOver.FallOver",
        // Ambient random animator-trigger loop (default trigger "Shine"), e.g. vent-hatch shine. Loops on a random
        // interval so it churns RNG, but it only fires a cosmetic animator trigger; RNG isolation is handled separately.
        "SetTriggerRandom.TriggerRoutine",
        // Camera follow/reposition — visual only, gameplay and physics don't depend on the camera.
        "CameraController.DoPositionToHero",
        // Area-title card display timer — UI only.
        "AreaTitleController.VisitPause",
        // Music cue application — audio only (cf. the separately RNG-isolated AudioManager.BeginApplyAtmosCue).
        "AudioManager.BeginApplyMusicCue",
        // HUD interaction-prompt pooling: waits realtime, then recycles the prompt GameObject. UI lifecycle only.
        "PromptMarker.RecycleDelayed",
        // Decorative pushable chain (hanging lamps): re-enables the chain-link push colliders once the hero leaves
        // range. A one-way reaction to the hero — never pushes or damages the hero back.
        "ChainPushReaction.ReEnableLinks",
        // Delayed counterpart on the same chain: after a touch delay, deactivates the link push colliders (then
        // starts ReEnableLinks). Same cosmetic chain reaction, writes no hero/gameplay state.
        "ChainPushReaction.DisableLinksDelayed",
        // Depth-sort jitter: nudges the object's own transform.position.z by a tiny Random.Range amount to avoid
        // z-fighting. Z never affects 2D gameplay/physics; the RNG draw is isolated separately (RandomIsolationPatch).
        "SetZ.SetPosition",
        // Ambient world rumble: camera shake + sound + particles on a loop, picking a random rumble/sound
        // (GetRandomElement draws RNG). Reads the hero only to pause when dead; writes no gameplay state.
        "WorldRumbleManager.DoRumbles",
        // The scene-transition driver. Its suspension point (@sN) sits, during the visible camera-fade window, in the
        // `yield return operationHandle` async Addressables fetch — I/O that advances in wall-clock, so at the same TAS
        // frame the fetch is at a different byte-offset under fast-forward vs real-time. Critically checked, not assumed:
        // this @sN is the *only* non-RNG trace divergence between the two runs, and every gameplay probe (GmState, pos,
        // vel, anim, cState/flags, FSM states) stays byte-identical — the coroutine writes no hero/scene state in the
        // visible window (it only fetches bytes; the unload/activate/BeginScene it drives runs after the fade, inside
        // the gated frozen window, and lands identically). So the step is wall-clock I/O progress, gameplay-irrelevant —
        // the same accepted class as RNG churn — and is excluded from the trace, not papered over.
        "SceneLoad.BeginRoutine",
        // Clockwork hatchling spawn *presentation*: plays spawn audio (RandomAudioClipTable, isolated separately) +
        // the hatch animation and toggles its renderer, with the damager disabled during the sequence. The spawn
        // reveal, not the attack logic; its churn (audio RNG / anim timing) carries no hero/gameplay signal.
        "ClockworkHatchling.Spawn",
        // Inventory pane open/close audio fade — lerps an AudioSource volume to 0 then stops it. Audio only.
        "InventoryPaneFollowAudio.FadeOut",
        // Damage/hit sprite flash (white material tint over a few frames). Purely visual; carries no hero/gameplay
        // state. Shows up as a leftover on the hero at pre-load frames (a flash still fading from before the load),
        // shifting the Info coroutine list between runs — hide it.
        "SpriteFlash.FlashRoutine",
        // Ambient NPC idle/look/rest animation (background pilgrims, town residents): drives only the NPC's own
        // animation state / rest FSM bool; reads the hero's position solely to wait. No hero/gameplay write.
        "LookAnimNPC.Rest",
        // Ambient NPC reaction to the hero performing/singing: plays the NPC's sing/idle animations and toggles its
        // own control (OnSingStarted UnityEvent stays NPC-side). No hero/gameplay write.
        "HeroPerformanceSingReaction.Behaviour",
        // Decorative sprite-frame animation (floor/background decor): cycles a SpriteRenderer through its frames,
        // writing only rend.sprite. Draws RNG for its start delay/frame; no hero/gameplay state.
        "BasicSpriteAnimator.Animate",
        "InputField.CaretBlink",
    ];

    internal static bool IsDenylisted(string label) => Denylist.Contains(label);

    internal static IEnumerator Wrap(IEnumerator inner, string label, string owner, MonoBehaviour? ownerBehaviour) =>
        new TrackedRoutine(inner, label, owner, ownerBehaviour);

    internal static void Register(TrackedRoutine routine) => running.Add(routine);

    internal static void Unregister(TrackedRoutine routine) {
        running.Remove(routine);
        if (routine.Handle is { } handle) {
            byHandle.Remove(handle);
        }
    }

    /// Associate a started coroutine's handle with its wrapper, so StopCoroutine(handle) can find and unregister it.
    internal static void TrackHandle(TrackedRoutine routine, Coroutine handle) {
        if (routine.Finished) {
            return;
        }

        routine.Handle = handle;
        byHandle[handle] = routine;
    }

    internal static void StopByHandle(Coroutine handle) {
        if (byHandle.TryGetValue(handle, out var routine)) {
            routine.ForceFinish();
        }
    }

    internal static void StopAllFor(MonoBehaviour owner) {
        foreach (var routine in running.Where(r => r.Owner == owner).ToArray()) {
            routine.ForceFinish();
        }
    }

    /// Reliably stop every tracked coroutine whose label contains <paramref name="filter"/> (all if null/empty), using
    /// the recorded owner + Coroutine handle: StopCoroutine(handle) is the overload that actually stops a running
    /// coroutine, unlike StopCoroutine(IEnumerator). Returns each stopped coroutine's description (owner included) so a
    /// caller can see what — and on which owner — was running. Diagnostic for load-during-death.
    internal static List<string> KillMatching(string? filter) {
        var matched = running
            .Where(r => string.IsNullOrEmpty(filter) || r.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();

        var killed = new List<string>();
        foreach (var routine in matched) {
            killed.Add(routine.Describe());
            if (routine.Owner && routine.Handle is { } handle) {
                routine.Owner!.StopCoroutine(handle);
            } else {
                routine.ForceFinish();
            }
        }

        return killed;
    }

    /// Snapshot of the running coroutines' descriptions (safe to enumerate while the set is live on the same thread).
    internal static List<string> RunningDescriptions() {
        PruneDestroyed();
        return running.Select(r => r.Describe()).ToList();
    }

    /// A coroutine stops the moment Unity destroys the MonoBehaviour that started it (e.g. a scene reload on a savestate
    /// load), but Unity never drives another MoveNext on it — so the wrapper's Finish never fires and the dead routine
    /// lingers in the tracker forever, one leaked entry per load. That shows up as stale coroutines in the display/trace
    /// (a battle's DoStartBattle stuck @s1 across the whole run, its count growing each reload) and shifts the Info
    /// layout between runs. Drop any routine whose owner has been destroyed.
    private static void PruneDestroyed() {
        // RemoveWhere iterates the set in place (no per-frame allocation, unlike Where(...).ToArray()); we can't call
        // ForceFinish here because its Unregister would re-enter running.Remove mid-iteration, so mark done and drop the
        // handle mapping inline.
        running.RemoveWhere(routine => {
            if (!routine.OwnerDestroyed) {
                return false;
            }

            routine.MarkDone();
            if (routine.Handle is { } handle) {
                byHandle.Remove(handle);
            }

            return true;
        });
    }

    private static readonly Regex GeneratedName = new(@"<(\w+)>", RegexOptions.Compiled);

    // Per-type cache of the compiler-generated iterator's state field, so Describe (called every frame per running
    // coroutine) doesn't re-run reflection lookups. Value is null for types without the field.
    private static readonly Dictionary<Type, FieldInfo?> stateFields = new();

    internal static FieldInfo? StateField(Type type) {
        if (!stateFields.TryGetValue(type, out var field)) {
            stateFields[type] = field =
                type.GetField("<>1__state", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        return field;
    }

    /// A readable label for a coroutine's enumerator, e.g. "HeroController.EnterHeroSubFadeUp". Compiler-generated
    /// iterators are named "OwnerType+<Method>d__N"; hand-written IEnumerator classes fall back to their type name.
    internal static string LabelFor(IEnumerator inner) {
        var type = inner.GetType();
        var match = GeneratedName.Match(type.Name);
        if (match.Success && type.DeclaringType is { } declaring) {
            return $"{declaring.Name}.{match.Groups[1].Value}";
        }

        return type.Name;
    }
}

internal sealed class TrackedRoutine(IEnumerator inner, string label, string owner, MonoBehaviour? ownerBehaviour)
    : IEnumerator {
    internal string Label { get; } = label;

    /// The MonoBehaviour that started this coroutine, for StopAllCoroutines matching (null if it was destroyed / the
    /// start had no instance).
    internal MonoBehaviour? Owner { get; } = ownerBehaviour;

    /// Whether this coroutine was started by a MonoBehaviour instance (vs a static/no-instance start). Captured at
    /// construction while the owner is still alive, so we can later distinguish "owner destroyed" from "never had one".
    private readonly bool hadOwner = ownerBehaviour;

    /// True once the owning MonoBehaviour has been destroyed: Unity then silently stops the coroutine without a final
    /// MoveNext, so this is the only signal that the routine is dead and should be pruned.
    internal bool OwnerDestroyed => hadOwner && !Owner;

    /// The Coroutine handle StartCoroutine returned, set once known, so StopCoroutine(handle) can unregister us.
    internal Coroutine? Handle { get; set; }

    internal bool Finished => done;

    private bool registered;
    private bool done;

    /// Label plus the current suspension point: the generated iterator's <c>&lt;&gt;1__state</c> (which yield it is
    /// parked at) and what it is waiting on (WaitForSeconds' duration etc.) — so the display shows how far along and
    /// on what a coroutine is blocked.
    internal string Describe() {
        var desc = $"{Label} [{owner}]";
        if (CoroutineTracker.StateField(inner.GetType())?.GetValue(inner) is int state) {
            desc += $"@s{state}";
        }

        switch (inner.Current) {
            case WaitForSeconds wait:
                desc += $" wait {wait.GetFieldValue<float>("m_Seconds"):0.##}s";
                break;
            case WaitForSecondsRealtime waitRealtime:
                desc += $" waitRT {waitRealtime.waitTime:0.##}s";
                break;
        }

        return desc;
    }

    public bool MoveNext() {
        if (done) {
            return false;
        }

        if (!registered) {
            registered = true;
            CoroutineTracker.Register(this);
        }

        bool moved;
        try {
            moved = inner.MoveNext();
        } catch {
            Finish();
            throw;
        }

        if (!moved) {
            Finish();
        }

        return moved;
    }

    private void Finish() {
        if (!done) {
            done = true;
            CoroutineTracker.Unregister(this);
        }
    }

    /// Unregister a coroutine that was stopped externally (StopCoroutine/StopAllCoroutines) rather than by running to
    /// completion — those paths never call MoveNext again, so Finish would otherwise never fire.
    internal void ForceFinish() => Finish();

    /// Mark finished without touching the tracker collections. For the pruner, which removes us from `running` via
    /// HashSet.RemoveWhere and therefore must not have us re-enter running.Remove. Setting done also stops a later
    /// MoveNext from re-registering us.
    internal void MarkDone() => done = true;

    public object Current => inner.Current;
    public void Reset() => inner.Reset();
}

[HarmonyPatch]
internal static class CoroutineTrackerPatch {
    // Wrap coroutines whenever Studio is attached, so the info panel can show them live even outside a run. Gating on
    // the editor connection (rather than always) keeps the overhead off normal play, and keeps the tracked set — and
    // thus the traced Info string — empty during headless runs that have no Studio.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), typeof(IEnumerator))]
    private static void StartCoroutine(MonoBehaviour __instance, ref IEnumerator routine) {
        if (!CommunicationWrapper.Connected || routine is TrackedRoutine) {
            return;
        }

        if (__instance && IsIgnoredOwner(__instance)) {
            return;
        }

        var label = CoroutineTracker.LabelFor(routine);
        if (CoroutineTracker.IsDenylisted(label)) {
            return;
        }

        routine = CoroutineTracker.Wrap(routine, label, __instance ? __instance.gameObject.name : "?", __instance);
    }

    // Associate the returned Coroutine handle with the wrapper, so StopCoroutine(handle) can unregister it.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), typeof(IEnumerator))]
    private static void StartCoroutinePost(IEnumerator routine, Coroutine __result) {
        if (routine is TrackedRoutine tracked && __result != null) {
            CoroutineTracker.TrackHandle(tracked, __result);
        }
    }

    // StopCoroutine/StopAllCoroutines stop a coroutine without another MoveNext, so the wrapper never finishes itself.
    // Unregister it here, else it lingers in the tracker forever (see byHandle).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StopCoroutine), typeof(Coroutine))]
    private static void StopCoroutineByHandle(Coroutine routine) {
        if (routine != null) {
            CoroutineTracker.StopByHandle(routine);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StopCoroutine), typeof(IEnumerator))]
    private static void StopCoroutineByEnumerator(IEnumerator routine) {
        if (routine is TrackedRoutine tracked) {
            tracked.ForceFinish();
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StopAllCoroutines))]
    private static void StopAllCoroutines(MonoBehaviour __instance) {
        if (__instance) {
            CoroutineTracker.StopAllFor(__instance);
        }
    }

    private static readonly string[] IgnoredOwnerNames = [
        "Silkfly Ambient",
        "Collectable Item Pickup",
        // Extensions.TimerRoutine and FloatCurveAnimator.AnimationRoutine are generic helpers/base classes (can't
        // denylist by label — gameplay subclasses use them too), so ignore the cosmetic owners that drive them:
        // HeroShamanRuneEffect's spawn/camera-bloom visuals, UI arrows, and the "Light Effects" curve animator. The
        // last draws a random start delay (MinMaxFloat.GetRandomValue → UnityEngine.Random) but is a visual light
        // animation, not interesting for TASing; if that RNG ever churns gameplay it surfaces as a RandomState diff.
        "Shaman Rune",
        "Arrow Right",
        "Arrow Left",
        "Light Effects",
        // Ambient scene decor with no gameplay class attached (a dead pilgrim whose "Song" object runs a cosmetic
        // singing timer via Extensions.TimerRoutine).
        "Corpse Pilgrim",
        // Darkness-mask renderer: every coroutine on it is a cosmetic fade (FadeAlpha, plus the generic
        // Extensions.TimerRoutine it drives — the latter can't be denylisted by label). Its only persisted state
        // (hasBeenUncovered → SceneData) is captured and compared separately, so the coroutine churn carries no
        // determinism signal. The renderer's GameObject is actually named "remask right"/"remask left" in scenes
        // (the "Remasker" type name doesn't match, StartsWith is case-sensitive), which is why FadeAlpha is
        // denylisted by label above and the generic Extensions.TimerRoutine it drives is ignored by owner here.
        "Remasker",
        "remask right",
        "remask left",
        // Inventory tool-crest display visual (cf. the denylisted InventoryToolCrest.TransitionDisplayState) — its
        // generic Extensions.TimerRoutine is a cosmetic UI tween.
        "big_crest",
        // NestedFadeGroup alpha-fade container — its generic Extensions.TimerRoutine only tweens group alpha.
        "Fade Group",
        // Breakable-scenery debris fragments: their generic Extensions.TimerRoutine (AnimateRigidBody2DProperties)
        // only lerps the chunk's own rigidbody damping as it tumbles to rest — purely visual, no hero/gameplay contact.
        "Chunk",
        // Inventory UI border element — its generic Extensions.TimerRoutine is a cosmetic border tween.
        "Border",
        // Ambient water-drip decor (cf. the denylisted WaterDrip.Drip): the drip cycle also spawns the generic
        // Extensions.PlayAnimWait for its "Drip"/"Impact" anims, which can't be denylisted by label.
        "Water Drip",
        // Sign-hiding trigger: a generic TimedEventCaller (fires an OnCall UnityEvent on a random-delay loop, so it
        // draws RNG) that only disables a decorative signpost. Owner-scoped because the helper itself is generic.
        "Disable Sign",
    ];

    // Owner categories whose coroutines are provisionally ignored to cut display/trace noise while we focus on the
    // core owners (Hero, GameManagers, systems). TODO: each category *can* matter — revisit rather than skip forever.
    private static bool IsIgnoredOwner(MonoBehaviour mb) {
        // Enemies: attack windups / death timing can be gameplay-relevant; skipped for now.
        if (mb.GetComponentInParent<HealthManager>()) {
            return true;
        }

        // Owners whose coroutines are only cosmetic churn (ambient decor, a collectable's fade/glint timers), matched by
        // explicit name (Unity appends " (N)" to duplicates, so StartsWith). Only names we've actually seen churn — no
        // speculative markers. Hiding these loses no determinism signal: if one of their RNG draws ever leaked into
        // gameplay it would surface as a RandomState divergence in a savestate/trace assert, traceable from there.
        // TODO: name match is a heuristic; add entries as they show up, or switch to a component marker if one fits.
        var name = mb.gameObject.name;
        foreach (var ignored in IgnoredOwnerNames) {
            if (name.StartsWith(ignored)) {
                return true;
            }
        }

        return false;
    }
}
