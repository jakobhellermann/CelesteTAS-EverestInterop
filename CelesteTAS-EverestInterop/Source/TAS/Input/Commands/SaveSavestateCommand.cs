using System.Collections.Generic;
using System.IO;
using StudioCommunication;
using TAS.ModInterop;
using TAS.Playback;
using TAS.Tracer;

namespace TAS.Input.Commands;

/// Creates a PreciseSavestates savestate at the command's line position during playback. Synchronous (snapshot
/// capture), so playback does not pause — unlike a `***S` breakpoint. Complement to <see cref="LoadSavestateCommand"/>.
///
/// Usage in a TAS file: <c>SaveSavestate, &lt;slot&gt;</c>
internal static class SaveSavestateCommand {
    private const string CommandName = "SaveSavestate";

    private class SaveSavestateMeta : ITasCommandMeta {
        public string Insert => $"{CommandName}{CommandInfo.Separator}[0;slot]";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            yield break;
        }
    }

    [TasCommand(CommandName, LegalInFullGame = false, MetaDataProvider = typeof(SaveSavestateMeta))]
    private static void SaveSavestate(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (commandLine.Arguments.Length < 1) {
            PopupToast.ShowAndLog($"{CommandName}: missing slot argument");
            return;
        }

        if (PreciseSavestatesInterop.Instance is not { } interop) {
            PopupToast.ShowAndLog($"{CommandName}: PreciseSavestates is not installed");
            return;
        }

        var arg = commandLine.Arguments[0];
        TasTracer.TraceEvent($"Savestate save: {arg}");
        var created = SavestateLoad.IsPathArg(arg)
            ? interop.CreateSavestateToFile(SavestateLoad.ResolvePath(arg, filePath))
            : interop.CreateSavestate(arg, arg, SavestateLoad.Layer);

        // A failed capture (e.g. mid scene-transition: "Can't create savestate in state EXITING_LEVEL") must abort
        // the run rather than let it complete as if the savestate exists — a later Load/AssertEqualSavestates would
        // then fail obscurely, or worse, silently pass against a stale file. PreciseSavestates logs the reason.
        if (!created) {
            AbortTas($"{CommandName}: failed to create savestate '{arg}' ({Path.GetFileName(filePath)}:{fileLine})", log: true);
        }
    }
}
