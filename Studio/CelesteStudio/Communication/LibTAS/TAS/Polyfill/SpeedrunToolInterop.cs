using CelesteStudio.Communication.LibTAS;
using System;

namespace TAS.Utils;

public static class SpeedrunToolInterop {
    public static bool Installed => true;
    public static bool MultipleSaveSlotsSupported => true;

    public static bool SaveState(string slot) {
        if (LibTasCommunication.Instance is not { } comm) {
            return false;
        }

        Console.WriteLine($"Saving slot {slot}");
        
        const int idx = 0;
        comm.SendSavestateIndex(idx);
        comm.SendSavestatePath($"/tmp/Savestates/savestate-{slot}");
        return comm.SendSavestate();
    }

    public static bool LoadState(string slot) {
        if (LibTasCommunication.Instance is not { } comm) {
            return false;
        }

        Console.WriteLine($"\nLoading slot {slot}");

        const int idx = 0;
        comm.SendSavestateIndex(idx);
        var ok = comm.SendLoadstate();
        comm.SendExpose();

        return ok;
    }

    public static void ClearState(string slot) {
        Console.WriteLine($"Clearing slot {slot}");
    }
}
