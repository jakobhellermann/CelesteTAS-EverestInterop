// ReSharper disable all
#pragma warning disable CS8321 // Local function is declared but never used
namespace TAS.ModInterop;

public static class SpeedrunToolInterop {
    public static bool Installed => true; // TODO
    
    private static void AddSaveLoadAction() {
        // saveLoadAction = new SaveLoadAction(OnSave, OnLoad, OnClear);
        // SaveLoadAction.Add((SaveLoadAction) saveLoadAction);

        return;

        static void OnSave() {
            // tasStartInfo = MetadataCommands.TasStartInfo.DeepCloneShared();
            // mouseState = MouseCommand.CurrentState;
            // followers = HitboxSimplified.Followers.DeepCloneShared();
            // disallowUnsafeInput = SafeCommand.DisallowUnsafeInput;
        }

        static void OnLoad() {
            /*PressCommand.PressKeys.Clear();
            foreach (var keys in pressKeys!) {
                PressCommand.PressKeys.Add(keys);
            }*/

            // MetadataCommands.TasStartInfo = tasStartInfo.DeepCloneShared();
            // MouseCommand.CurrentState = mouseState;
            // SafeCommand.DisallowUnsafeInput = disallowUnsafeInput;
        }

        static void OnClear() {
            // pressKeys = null;
            // InfoWatchEntity.WatchedEntities_Save.Clear();
        }
    }
}
