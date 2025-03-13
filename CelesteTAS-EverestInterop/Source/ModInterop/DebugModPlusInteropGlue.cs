using System;
using System.Linq;
using TAS.Utils;

namespace TAS.ModInterop;

[Flags]
public enum SavestateFilter {
    None = 0,
    Flags = 1 << 1,
    Player = 1 << 2,
    Monsters = 1 << 3,

    // ReSharper disable once InconsistentNaming
    FSMs = 1 << 4,

    All = Flags | Player | FSMs | Monsters,
}

public class Savestate {
    internal readonly object Inner;

    internal Savestate(object inner) {
        this.Inner = inner;
    }
}

public class DebugModPlusInteropGlue(Type interop) {
    public static DebugModPlusInteropGlue? Load() {
        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .LastOrDefault(assembly => assembly.FullName.StartsWith("DebugModPlus"));
        var interop = asm?.GetType("DebugModPlus.Interop.DebugModPlusInterop");
        return interop != null ? new DebugModPlusInteropGlue(interop) : null;
    }
    
    public Savestate CreateSavestate(SavestateFilter savestateFilter) {
        return new Savestate(interop.InvokeMethod<object>("CreateSavestate", [(int)savestateFilter])!);
    }
    public bool LoadSavestate(Savestate savestate) {
        return interop.InvokeMethod<bool>("LoadSavestate", [savestate.Inner]);
    }
    

    public Savestate CreateSavestateDisk(string name, string? layer, SavestateFilter savestateFilter) {
        return new Savestate(interop.InvokeMethod<object>("CreateSavestateDisk", [name, layer, (int)savestateFilter])!);
    }
    public void LoadSavestateDisk(string name, string? layer = null) {
        interop.InvokeMethod<object>("LoadSavestateDisk", [name, layer]);
    }

    public string[] ListSavestates(string? layer = null) {
        return interop.InvokeMethod<string[]>("ListSavestates", [layer])!;
    }
    
    public bool IsLoadingSavestate => interop.GetFieldValue<bool>("IsLoadingSavestate");
}
