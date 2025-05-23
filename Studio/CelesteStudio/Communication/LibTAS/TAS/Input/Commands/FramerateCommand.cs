using System;
using CelesteStudio.Communication.LibTAS.TAS;
using StudioCommunication;

namespace TAS.Input.Commands;

public static class FramerateCommand {
    [TasCommand("Framerate:")]
    private static void Framerate(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (args.Length != 1) {
            AbortTas("Expected framerate argument");
            return;
        }

        if (!uint.TryParse(args[0], out uint framerate)) {
            AbortTas($"Invalid float {args[0]}");
            return;
        }
        Console.WriteLine($"Framerate now set to {framerate}");

        InputHelper.Framerate = framerate;
    }
}
