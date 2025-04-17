using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Microsoft.Xna.Framework.Input;
using StudioCommunication;
using TAS.Input;
using TAS.Tools;
using TAS.UnityInterop;

namespace TAS.Communication;

public static class CommunicationWrapper {

    public static bool Connected => comm is { Connected: true };
    private static CommunicationAdapterCeleste? comm;

    /*[Load]
    private static void Load() {
        Everest.Events.Celeste.OnExiting += Stop;
    }
    [Unload]
    private static void Unload() {
        Everest.Events.Celeste.OnExiting -= Stop;
        Stop();
    }*/

    public static void Start() {
        if (SyncChecker.Active) {
            // Prevent Studio connection while sync-checking
            return;
        }

        if (comm != null) {
            "Tried to start the communication adapter while already running!".Log(LogLevel.Warning);
            return;
        }

        comm = new CommunicationAdapterCeleste();
    }

    public static void Stop() {
        if (SyncChecker.Active) {
            // Prevent Studio connection while sync-checking
            return;
        }

        if (comm == null) {
            Log.Warn("Tried to stop the communication adapter while not running!");
            return;
        }

        comm.Dispose();
        comm = null;
    }

    public static void SendReset() {
        if (!Connected) return;

        comm!.WriteReset();
    }

    public static void ChangeStatus() {
        if (TasSettings.AttemptConnectStudio && comm == null) {
            Start();
        } else if (comm != null) {
            Stop();
        }
    }

    #region Actions

    public static void SendState(StudioState state) {
        if (!Connected) {
            return;
        }

        comm!.WriteState(state);
    }
    public static void SendUpdateLines(Dictionary<int, string> updateLines) {
        if (!Connected) {
            return;
        }

        comm!.WriteUpdateLines(updateLines);
    }
    public static void SendCurrentBindings() {
        if (!Connected) {
            return;
        }

        Dictionary<int, List<int>> nativeBindings = Hotkeys.StudioHotkeys.ToDictionary(pair => (int) pair.Key, pair => pair.Value.Select(x => x.ToXNA()).Cast<int>().ToList());
        comm!.WriteCurrentBindings(nativeBindings);
    }
    public static void SendRecordingFailed(RecordingFailedReason reason) {
        if (!Connected) {
            return;
        }

        comm!.WriteRecordingFailed(reason);
    }
    public static void SendSettings(StudioCommunication.GameSettings settings) {
        if (!Connected) {
            return;
        }

        comm!.WriteSettings(settings);
    }
    public static void SendCommandList() {
        if (!Connected) {
            return;
        }

        comm!.WriteCommandList(Command.GetCommandList());
    }

    #endregion
}
