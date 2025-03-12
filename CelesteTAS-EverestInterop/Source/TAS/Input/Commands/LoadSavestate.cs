using System.Collections.Generic;
using StudioCommunication;
using TAS.Tracer;

namespace TAS.Input.Commands;

public static class LoadSavestateCommand {
    private class LoadSavestateMeta : ITasCommandMeta {
        public string Insert =>
            $"load_savestate{CommandInfo.Separator}[0;savestate]";

        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath,
            int fileLine) {
            if (!GameCore.IsAvailable()) yield break;

            if (DebugModPlusInterop is not { } interop) yield break;

            foreach (var savestate in interop.ListSavestates()) {
                yield return new CommandAutoCompleteEntry { Name = savestate, IsDone = true };
            }
        }
    }

    [TasCommand("load_savestate", MetaDataProvider = typeof(LoadSavestateMeta))]
    private static void LoadSavestate(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (commandLine.Arguments.Length != 1)
            AbortTas($"Invalid number of arguments in load command: '{commandLine.OriginalText}'.");

        var name = commandLine.Arguments[0];

        if (!GameCore.IsAvailable() || GameCore.Instance.gameLevel == null) {
            AbortTas("Attempted to start TAS outside of a level");
            return;
        }

        if (DebugModPlusInterop is not { } interop) {
            AbortTas("Attempted to load savestate without DebugModPlus installed");
            return;
        }


        /*var snapshot = new AnimatorSnapshot {
           StateHash = 1432961145,
           NormalizedTime = 0,
           ParamsFloat = new Dictionary<int, float>(),
           ParamsInt = new Dictionary<int, int>(),
           ParamsBool = new Dictionary<int, bool>(),
       };
        InputHelper.WithPrevent(() => {
            snapshot.Restore(Player.i.animator);
            Player.i.animator.Update(0);
        });*/


        interop.LoadSavestateDisk(name);

        TasTracer.TraceEvent("LoadSavestate");

        // TODO savestate
        /*var task = DebugModPlus.DebugModPlus.Instance.SavestateModule.LoadSavestateAt(fullName);
        if (!task.IsCompleted) {
            ToastManager.Toast("Did not load savestate in a single frame");
        }*/
        /*
         var snapshot = new AnimatorSnapshot {
            StateHash = 1432961145,
            NormalizedTime = 0,
            ParamsFloat = new Dictionary<int, float>(),
            ParamsInt = new Dictionary<int, int>(),
            ParamsBool = new Dictionary<int, bool>(),
        };
        snapshot.Restore(Player.i.animator);*/

        // foreach(var condition in ConditionTimer.Instance.AllConditions) {
        // condition.SetFieldValue("_isFalseTimer", float.PositiveInfinity);
        // }

        // CameraManager.Instance.ResetCamera2DDockerToPlayer();
        // CameraManager.Instance.camera2D.CenterOnTargets();
        // CameraManager.Instance.dummyOffset = Vector2.SmoothDamp(
        // SingletonBehaviour<CameraManager>.Instance.dummyOffset, direction, ref this.currentV, 0.25f);
    }
}
