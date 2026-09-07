using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using JetBrains.Annotations;
using TAS.Utils;

namespace TAS.ModInterop;

/// Typed binding to the PreciseSavestates plugin's `Interop` API, resolved via reflection.
///
/// Obtained through <see cref="Instance"/>, which is null until PreciseSavestates is loaded — ScriptEngine
/// loads plugins in arbitrary order (it ignores [BepInDependency]), so the dependency may not be registered
/// in Chainloader.PluginInfos yet while our Awake runs. Resolution is cached only once it succeeds.
///
/// Once you hold an instance, the bound methods invoke directly and throw on a signature mismatch rather
/// than failing silently.
[PublicAPI]
public class PreciseSavestatesInterop {
    /// The interop wrapper, or null if PreciseSavestates isn't loaded (yet).
    public static PreciseSavestatesInterop? Instance {
        get {
            if (field != null) {
                return field;
            }
            if (ModUtils.GetPlugin(ModUtils.PreciseSavestatesId) is not { } plugin) {
                return null;
            }

            return field = new PreciseSavestatesInterop(plugin);
        }
    }

    public static bool Installed => Instance != null;

    private readonly object interop;
    private readonly MethodInfo createSavestate;
    private readonly MethodInfo createSavestateToFile;
    private readonly MethodInfo loadSavestate;
    private readonly MethodInfo loadSavestateFromFile;
    private readonly MethodInfo deleteSavestate;
    private readonly MethodInfo hasSavestate;
    private readonly MethodInfo listSlots;
    private readonly PropertyInfo lastLoadedGameTime;
    private readonly PropertyInfo lastLoadedFrameCount;
    private readonly PropertyInfo lastLoadedRandomState;
    private readonly PropertyInfo deferSnapshotRestore;
    private readonly PropertyInfo snapshotPending;
    private readonly MethodInfo applyPendingSnapshot;

    private PreciseSavestatesInterop(BaseUnityPlugin plugin) {
        interop = plugin.GetFieldValue<object>("Interop")
                  ?? throw new InvalidOperationException("PreciseSavestates plugin has no 'Interop' field");

        var type = interop.GetType();
        createSavestate = Resolve(type, "CreateSavestate", typeof(string), typeof(string), typeof(string), typeof(int));
        createSavestateToFile = Resolve(type, "CreateSavestateToFile", typeof(string), typeof(int));
        loadSavestate = Resolve(type, "LoadSavestate", typeof(string), typeof(string));
        loadSavestateFromFile = Resolve(type, "LoadSavestateFromFile", typeof(string));
        deleteSavestate = Resolve(type, "DeleteSavestate", typeof(string), typeof(string));
        hasSavestate = Resolve(type, "HasSavestate", typeof(string), typeof(string));
        listSlots = Resolve(type, "ListSlots", typeof(string));
        lastLoadedGameTime = ResolveProperty(type, "LastLoadedGameTime");
        lastLoadedFrameCount = ResolveProperty(type, "LastLoadedFrameCount");
        lastLoadedRandomState = ResolveProperty(type, "LastLoadedRandomState");
        deferSnapshotRestore = ResolveProperty(type, "DeferSnapshotRestore");
        snapshotPending = ResolveProperty(type, "SnapshotPending");
        applyPendingSnapshot = Resolve(type, "ApplyPendingSnapshot");
    }

    private static MethodInfo Resolve(Type type, string name, params Type[] parameterTypes) =>
        type.GetMethodInfo(name, parameterTypes, logFailure: false)
        ?? throw new InvalidOperationException(
            $"PreciseSavestates.Interop is missing {name}({string.Join(", ", parameterTypes.Select(t => t.Name))}) — version mismatch?");

    private static PropertyInfo ResolveProperty(Type type, string name) =>
        type.GetProperty(name)
        ?? throw new InvalidOperationException($"PreciseSavestates.Interop is missing property {name} — version mismatch?");

    public bool CreateSavestate(string name, string slot, string? layer = null, int filter = -1) =>
        (bool) createSavestate.Invoke(interop, [name, slot, layer, filter])!;

    public bool CreateSavestateToFile(string path, int filter = -1) =>
        (bool) createSavestateToFile.Invoke(interop, [path, filter])!;

    public Task<bool> LoadSavestate(string? slot = null, string? layer = null) =>
        (Task<bool>) loadSavestate.Invoke(interop, [slot, layer])!;

    public Task<bool> LoadSavestateFromFile(string path) =>
        (Task<bool>) loadSavestateFromFile.Invoke(interop, [path])!;

    public void DeleteSavestate(string? slot = null, string? layer = null) =>
        deleteSavestate.Invoke(interop, [slot, layer]);

    public bool HasSavestate(string? slot = null, string? layer = null) =>
        (bool) hasSavestate.Invoke(interop, [slot, layer])!;

    public string[] ListSlots(string? layer = null) =>
        (string[]) listSlots.Invoke(interop, [layer])!;

    public float? LastLoadedGameTime => (float?) lastLoadedGameTime.GetValue(interop);
    public int? LastLoadedFrameCount => (int?) lastLoadedFrameCount.GetValue(interop);
    public UnityEngine.Random.State? LastLoadedRandomState => (UnityEngine.Random.State?) lastLoadedRandomState.GetValue(interop);

    public bool DeferSnapshotRestore {
        get => (bool) deferSnapshotRestore.GetValue(interop)!;
        set => deferSnapshotRestore.SetValue(interop, value);
    }

    public bool SnapshotPending => (bool) snapshotPending.GetValue(interop)!;

    public void ApplyPendingSnapshot() => applyPendingSnapshot.Invoke(interop, []);
}
