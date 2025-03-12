using StudioCommunication;
using TAS.ModInterop;

namespace TAS.Input.Commands;

public static class CreateSavestateCommand {
    private class CreateSavestateMeta : ITasCommandMeta {
        public string Insert => "dump_savestate";

        public bool HasArguments => false;
    }

    [TasCommand("dump_savestate", MetaDataProvider = typeof(CreateSavestateMeta))]
    private static void DumpSavestate(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (commandLine.Arguments.Length != 0)
            AbortTas($"Invalid number of arguments in dump_savestate command: '{commandLine.OriginalText}'.");

        DebugModPlusInterop!.CreateSavestateDisk("tas-dump", null, SavestateFilter.Player | SavestateFilter.Monsters);
    }
}
