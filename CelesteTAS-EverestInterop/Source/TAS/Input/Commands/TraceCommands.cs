using StudioCommunication;
using TAS.Tracer;

namespace TAS.Input.Commands;

public static class TraceCommands {
    [TasCommand("BeginTrace", ExecuteTiming = ExecuteTiming.Runtime)]
    private static void BeginTrace(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        var name = commandLine.Arguments.Length > 0 ? commandLine.Arguments[0] : "trace";
        TasTracer.BeginSegment(name);
    }

    [TasCommand("EndTrace", ExecuteTiming = ExecuteTiming.Runtime)]
    private static void EndTrace(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        TasTracer.EndSegment();
    }

    // Register a per-frame reflection probe into the trace, e.g. `TraceVar, HeroController.cState.onGround` — adds a
    // trace variable live from the TAS with no rebuild. Multiple paths per command. Cleared at the start of each run.
    [TasCommand("TraceVar", ExecuteTiming = ExecuteTiming.Runtime)]
    private static void TraceVar(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        foreach (var arg in commandLine.Arguments) {
            TasTracer.AddTraceVar(arg);
        }
    }
}
