using StudioCommunication;

namespace TAS.Input.Commands;

public static class FramerateCommand {
    private class FramerateMeta : ITasCommandMeta {
        public override string Insert => $"Framerate{CommandInfo.Separator}[0;60]";

        public override bool HasArguments => false;
    }

    [TasCommand("Framerate", MetaDataProvider = typeof(FramerateMeta))]
    private static void Framerate(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (commandLine.Arguments.Length != 1)
            AbortTas($"Invalid number of arguments in framerate command: '{commandLine.OriginalText}'.");

        if (!int.TryParse(commandLine.Arguments[0], out var framerate))
            AbortTas($"Not a valid number: '{commandLine.Arguments[0]}'.");

        InputHelper.SetTasFramerate(framerate);
    }
}
