using System;
using System.Collections.Generic;
using System.IO;
using TAS.Input;

namespace TAS.Tools;

/// Automatically runs the specified TAS files and reports if they were successful
internal static class SyncChecker {
    /// Whether the game is in sync-check mode and disallows user intervention
    public static bool Active { get; private set; } = false;

    /// Promotes invalid room labels from a warning to an error
    public static bool ValidateRoomLabels = false;

    private static bool waitingForLoad = true;

    private static readonly Queue<string> fileQueue = [];
    private static string resultFile = string.Empty;

    private static SyncCheckResult.Status currentStatus = SyncCheckResult.Status.Success;
    private static SyncCheckResult.AdditionalInfo currentAdditionalInformation = new();

    private static SyncCheckResult result = new();

    private static string? CurrentFilePath => Manager.Controller.Current?.FilePath;
    private static int? CurrentFileLine => Manager.Controller.Current?.FileLine;

    public static void AddFile(string file) {
        Active = true;

        if (!File.Exists(file)) {
            Log.Error("CelesteTAS/SyncCheck", $"TAS file to sync check was not found: '{file}'");
            return;
        }

        Log.Info("CelesteTAS/SyncCheck", $"Registered file for sync checking: '{file}'");

        fileQueue.Enqueue(file);
    }
    public static void SetResultFile(string file) {
        Active = true;

        if (!string.IsNullOrEmpty(resultFile)) {
            Log.Warn("CelesteTAS/SyncCheck", $"Overwriting previously defined result file '{resultFile}' with '{file}");
        } else {
            Log.Info("CelesteTAS/SyncCheck", $"Writing sync-check result to file: '{file}'");
        }

        resultFile = file;
    }

    private static bool IsFinished() => true;

    /// Indicates that the current TAS has finished executing
    public static void ReportRunFinished() {
        if (!Active) {
            return;
        }

        Log.Info("CelesteTAS/SyncCheck", $"Finished check for file: '{Manager.Controller.FilePath}'");

        // Check for desyncs
        if (currentStatus == SyncCheckResult.Status.Success && !IsFinished()) {
            // TAS did not finish
            currentStatus = SyncCheckResult.Status.NotFinished;
            currentAdditionalInformation.Clear();
            currentAdditionalInformation.Abort = new SyncCheckResult.AbortInfo(CurrentFilePath, CurrentFileLine, Manager.Controller.Current?.ToString());
        }

        GameInfo.Update();

        /*string infoWithSid = Engine.Scene?.GetSession() is { } session
            ? $"{GameInfo.ExactStatus}\n\nSID: {session.Area} ({session.MapData.Filename})"
            : GameInfo.ExactStatus;*/
        string infoWithSid = GameInfo.StudioInfo;
        var entry = new SyncCheckResult.Entry(Manager.Controller.FilePath, currentStatus, infoWithSid, currentAdditionalInformation);

        result.Entries.Add(entry);
        result.WriteToFile(resultFile);

        if (fileQueue.TryDequeue(out string? file)) {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Add to action queue, since we're still in DisableRun and can't start the next one immediately
            Manager.AddMainThreadAction(() => CheckFile(file));
        } else {
            // Done with all checks
            result.Finished = true;
            result.WriteToFile(resultFile);

            Environment.Exit(0);
        }
    }

    /// Indicates that a time command was updated with another time
    public static void ReportWrongTime(string filePath, int fileLine, string oldTime, string newTime) {
        if (!Active) {
            return;
        }

        Log.Error("CelesteTAS/SyncCheck", $"Detected wrong time in file '{filePath}' line {fileLine}: '{oldTime}' vs '{newTime}'");

        if (currentStatus != SyncCheckResult.Status.WrongTime) {
            currentStatus = SyncCheckResult.Status.WrongTime;
            currentAdditionalInformation.Clear();
            currentAdditionalInformation.WrongTime = [];
        }
        currentAdditionalInformation.WrongTime!.Add(new SyncCheckResult.WrongTimeInfo(filePath, fileLine, oldTime, newTime));
    }

    /// Indicates that an unsafe action was performed in safe-mode
    public static void ReportUnsafeAction() {
        if (!Active) {
            return;
        }

        Log.Error("CelesteTAS/SyncCheck", "Detected unsafe action");

        currentStatus = SyncCheckResult.Status.UnsafeAction;
        currentAdditionalInformation.Clear();
        currentAdditionalInformation.Abort = new SyncCheckResult.AbortInfo(CurrentFilePath, CurrentFileLine, Manager.Controller.Current?.ToString());
    }

    /// Indicates that an Assert-command failed
    public static void ReportAssertFailed(string lineText, string filePath, int fileLine, string expected, string actual) {
        if (!Active) {
            return;
        }

        Log.Error("CelesteTAS/SyncCheck", $"Detected failed assertion '{lineText}' in file '{filePath}' line {fileLine}: Expected '{expected}', got '{actual}'");

        currentStatus = SyncCheckResult.Status.UnsafeAction;
        currentAdditionalInformation.Clear();
        currentAdditionalInformation.AssertFailed = new SyncCheckResult.AssertFailedInfo(filePath, fileLine, actual, expected);
    }

    /// Indicates that a crash happened while sync-checking
    public static void ReportCrash(string ex) {
        if (!Active) {
            return;
        }

        Log.Error("CelesteTAS/SyncCheck", $"Detected a crash: {ex}");

        currentStatus = SyncCheckResult.Status.Crash;
        currentAdditionalInformation.Clear();
        currentAdditionalInformation.Crash = new SyncCheckResult.CrashInfo(CurrentFilePath, CurrentFileLine, ex);
    }

    [Initialize]
    private static void Initialize() {
    }
    [Unload]
    private static void Unload() {
    }

    /// Starts executing a TAS for sync-checking
    private static void CheckFile(string file) {
        // Reset state
        currentStatus = SyncCheckResult.Status.Success;
        currentAdditionalInformation.Clear();

        Log.Info("CelesteTAS/SyncCheck", $"Starting check for file: '{file}'");

        Manager.Controller.FilePath = file;
        Manager.EnableRun();
    }

    [ParseFileEnd]
    private static void ParseFileEnd() {
        if (!Active) {
            return;
        }

        var controller = Manager.Controller;
        if (controller.Inputs.Count == 0) {
            ReportRunFinished();
            return;
        }

        // Insert breakpoint at the end
        controller.FastForwards[controller.Inputs.Count] = new FastForward(controller.Inputs.Count, StudioLine: 0, FilePath: string.Empty, FileLine: 0);
    }
}
