using System;
using System.Collections.Generic;
using System.Linq;
using StudioCommunication;
using TAS.ModInterop;
using TAS.Playback;
using TAS.Utils;

namespace TAS.Input.Commands;

/// Loads a PreciseSavestates savestate at the start of a TAS, providing a deterministic starting point
/// (the load includes the scene transition). Decoupled from the in-file `***S` savestate machinery.
///
/// The load runs at TAS start (not at the command's line position), so placement in the file doesn't matter.
///
/// Usage in a TAS file: <c>StartOnSavestate, &lt;slot&gt;</c>
internal static class StartOnSavestateCommand {
    private const string CommandName = "StartOnSavestate";

    private class StartOnSavestateMeta : ITasCommandMeta {
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

    // Only registered so the line parses into Controller.Commands; the load itself runs in OnEnableRun,
    // not here, so that merely parsing the file (Studio refreshes, sync checks) doesn't trigger a scene load.
    [TasCommand(CommandName, LegalInFullGame = false, ExecuteTiming = ExecuteTiming.Parse, MetaDataProvider = typeof(StartOnSavestateMeta))]
    private static void StartOnSavestate(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        // no-op
    }

    [EnableRun]
    private static void OnEnableRun() {
        // Read the directive straight from the freshly-parsed controller — stateless, so a stale value from
        // a previously-parsed file can't leak in.
        var command = Manager.Controller.Commands.Values
            .SelectMany(commands => commands)
            .FirstOrDefault(command => command.Is(CommandName));
        if (command.Attribute == null) {
            return; // no StartOnSavestate in this TAS
        }

        var args = command.Args;
        if (args.Length < 1) {
            PopupToast.ShowAndLog($"{CommandName}: missing slot argument");
            return;
        }

        SavestateLoad.Load(args[0], CommandName);
    }
}
