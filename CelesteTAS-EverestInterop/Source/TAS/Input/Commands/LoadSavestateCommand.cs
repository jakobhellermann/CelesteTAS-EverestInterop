using System.Collections.Generic;
using StudioCommunication;
using TAS.ModInterop;
using TAS.Playback;

namespace TAS.Input.Commands;

/// Loads a PreciseSavestates savestate at the command's line position during playback (unlike
/// <see cref="StartOnSavestateCommand"/>, which loads once at TAS start). Lets a TAS jump mid-run to a saved
/// point — e.g. to replay a later section without playing the lead-up.
///
/// Playback pauses for the async load (the IsLoading gate in Manager.Update) and resumes from the restored
/// state on the next frame. See <see cref="SavestateLoad"/> for the shared load machinery.
///
/// Like Celeste's load commands, this consumes the frame it lands on (the command shares that frame with the
/// following input line, and the load discards it). Put a throwaway `1` right after it so the real input that
/// follows plays in full:
/// <code>
/// LoadSavestate, &lt;slot&gt;
///    1
///   10,R
/// </code>
///
/// Usage in a TAS file: <c>LoadSavestate, &lt;slot&gt;</c>
internal static class LoadSavestateCommand {
    private const string CommandName = "LoadSavestate";

    private class LoadSavestateMeta : ITasCommandMeta {
        public string Insert => $"{CommandName}{CommandInfo.Separator}[0;slot]";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length != 1 || PreciseSavestatesInterop.Instance is not { } interop) {
                yield break;
            }

            foreach (string slot in interop.ListSlots(SavestateLoad.Layer)) {
                yield return new CommandAutoCompleteEntry { Name = slot, IsDone = true, HasNext = false };
            }
        }
    }

    // Default (runtime) timing: runs when playback reaches this line, not at parse.
    [TasCommand(CommandName, LegalInFullGame = false, MetaDataProvider = typeof(LoadSavestateMeta))]
    private static void LoadSavestate(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (commandLine.Arguments.Length < 1) {
            PopupToast.ShowAndLog($"{CommandName}: missing slot argument");
            return;
        }

        var arg = commandLine.Arguments[0];
        if (SavestateLoad.IsPathArg(arg)) {
            SavestateLoad.LoadFile(SavestateLoad.ResolvePath(arg, filePath), CommandName);
        } else {
            SavestateLoad.Load(arg, CommandName);
        }
    }
}
