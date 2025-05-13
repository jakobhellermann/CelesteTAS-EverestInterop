using NineSolsAPI;
using System;
using System.IO;
using System.Linq;
using StudioCommunication;
using TAS.Communication;
using TAS.Tools;
using TAS.Utils;

namespace TAS.Input.Commands;

/// Commands which don't influence gameplay at all and just provide information to the user
internal static class MetadataCommands {
    // Track starting conditions for TAS to properly calculate (Midway)FileTime
    // public static (long FileTimeTicks, int FileSlot)? TasStartInfo;

    /// Total real-time frames in the TAS, without loading times
    // internal static (int FrameCount, int FileSlot)? RealTimeInfo = null;

    /*[Load]
    private static void Load() {
        On.Celeste.Level.Begin += LevelOnBegin;
        On.Celeste.Level.UpdateTime += LevelOnUpdateTime;
        Everest.Events.Level.OnComplete += UpdateChapterTime;

        typeof(Level)
            .GetMethodInfo(nameof(Level.Begin))!
            .HookAfter(StartFileTime);
        typeof(Level)
            .GetMethodInfo(nameof(Level.UpdateTime))!
            .HookAfter(StartFileTime);

        typeof(Celeste.Celeste)
            .GetMethodInfo(nameof(Celeste.Celeste.Update))!
            .HookBefore(() => {
                if (!Manager.Running || SaveData.Instance is not { } saveData) {
                    return;
                }

                // Advance real-time
                if (RealTimeInfo != null && (Manager.CurrState != Manager.State.Paused || GameInterop.IsLoading()) && !GameInterop.IsActuallyLoading()) {
                    RealTimeInfo = RealTimeInfo.Value with { FrameCount = RealTimeInfo.Value.FrameCount + 1 };
                }

                if (RealTimeInfo == null || RealTimeInfo.Value.FileSlot != saveData.FileSlot) {
                    RealTimeInfo = (0, saveData.FileSlot);
                }
            });

        static void StartFileTime() {
            if (!Manager.Running || SaveData.Instance is not { } saveData) {
                return;
            }

            if (TasStartInfo == null || TasStartInfo.Value.FileSlot != saveData.FileSlot) {
                TasStartInfo = (saveData.Time, saveData.FileSlot);
            }
        }
    }

    [Unload]
    private static void Unload() {
        Everest.Events.Level.OnComplete -= UpdateChapterTime;
    }

    [EnableRun]
    private static void ResetRealTime() {
        RealTimeInfo = null;
    }

    [DisableRun]
    private static void UpdateTimes() {
        if (TasStartInfo != null && SaveData.Instance != null && !Manager.Controller.CanPlayback) {
            UpdateAllMetadata("FileTime", _ => GameInfo.FormatTime(SaveData.Instance.Time - TasStartInfo.Value.FileTimeTicks));
        }
        if (RealTimeInfo != null && !Manager.Controller.CanPlayback) {
            UpdateAllMetadata("RealTime", _ => $"{TimeSpan.FromSeconds(RealTimeInfo.Value.FrameCount / 60.0f).ShortGameplayFormat()}({RealTimeInfo.Value.FrameCount})");
        }

        TasStartInfo = null;
        RealTimeInfo = null;
    }

    private static void UpdateChapterTime(Level level) {
        if (!Manager.Running || !level.Session.StartedFromBeginning) {
            return;
        }

        UpdateAllMetadata("ChapterTime", _ => GameInfo.GetChapterTime(level));
    }
*/

    private class RecordCountMeta : ITasCommandMeta {
        public string Insert => "RecordCount: 1";
        public bool HasArguments => false;
    }

    [TasCommand("RecordCount", Aliases = ["RecordCount:", "RecordCount："], CalcChecksum = false, MetaDataProvider = typeof(RecordCountMeta))]
    private static void RecordCountCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        // dummy
    }

    [TasCommand("FileTime", Aliases = ["FileTime:", "FileTime："], CalcChecksum = false)]
    private static void FileTimeCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        // dummy
    }

    [TasCommand("ChapterTime", Aliases = ["ChapterTime:", "ChapterTime："], CalcChecksum = false)]
    private static void ChapterTimeCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        // dummy
    }

    [TasCommand("RealTime", Aliases = ["RealTime:", "RealTime："], CalcChecksum = false)]
    private static void RealTimeCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        // dummy
    }

    
    private static MonsterBase DamageSectionTarget => MonsterManager.Instance.ClosetMonster;
    
    public static DamageSection UpdateDamageSection() {
        var section = new DamageSection(
            Manager.Controller.CurrentFrameInTas,
            DamageSectionTarget.postureSystem.PostureValue,
            DamageSectionTarget.postureSystem.InternalInjury
        );
        damageSectionStart = section;
        return section;
    }

    internal record struct DamageSection(int Frame, float HpBase, float HpInternal);

    private static DamageSection? damageSectionStart;

    [TasCommand("Damage:", CalcChecksum = false)]
    private static void DamageCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        var monster = MonsterManager.Instance.ClosetMonster;

        var before = damageSectionStart ??= new DamageSection(0, monster.postureSystem.MaxPostureValue, 0);
        var after = UpdateDamageSection();
        
        var time = after.Frame - before.Frame;
        var diffBase = before.HpBase - after.HpBase;
        var diffInternal = after.HpInternal - before.HpInternal;
        var diffRegular = diffBase - diffInternal;

        var dps = diffBase / time * InputHelper.CurrentTasFramerate;

        var damageText = $"{diffRegular}";
        if (diffInternal != 0) damageText += $" (+{diffInternal})";
        damageText += $" in {time}f = {dps:0.00} DPS";

        UpdateAllMetadata(
            "Damage:",
            _ => damageText,
            Manager.Controller.CurrentCommands.Contains);
    }

    /*
    [TasCommand("MidwayFileTime", Aliases = ["MidwayFileTime:", "MidwayFileTime："], CalcChecksum = false)]
    private static void MidwayFileTimeCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (TasStartInfo == null || SaveData.Instance == null) {
            return;
        }

        UpdateAllMetadata("MidwayFileTime",
            _ => GameInfo.FormatTime(SaveData.Instance.Time - TasStartInfo.Value.FileTimeTicks),
            command => Manager.Controller.CurrentCommands.Contains(command));
    }

    [TasCommand("MidwayChapterTime", Aliases = ["MidwayChapterTime:", "MidwayChapterTime："], CalcChecksum = false)]
    private static void MidwayChapterTimeCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (!Manager.Running || Engine.Scene is not Level level) {
            return;
        }

        UpdateAllMetadata("MidwayChapterTime",
            _ => GameInfo.GetChapterTime(level),
            command => Manager.Controller.CurrentCommands.Contains(command));
    }

    [TasCommand("MidwayRealTime", Aliases = ["MidwayRealTime:", "MidwayRealTime："], CalcChecksum = false)]
    private static void MidwayRealTimeCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (RealTimeInfo == null) {
            return;
        }

        UpdateAllMetadata("MidwayRealTime",
            _ => $"{TimeSpan.FromSeconds(RealTimeInfo.Value.FrameCount / 60.0f).ShortGameplayFormat()}({RealTimeInfo.Value.FrameCount})",
            command => Manager.Controller.CurrentCommands.Contains(command));
    }
    */

    private static void UpdateAllMetadata(string commandName, Func<Command, string> getMetadata, Func<Command, bool>? predicate = null) {
        string tasFilePath = Manager.Controller.FilePath;
        var metadataCommands = Manager.Controller.Commands.SelectMany(pair => pair.Value)
            .Where(command => command.Is(commandName) && command.FilePath == Manager.Controller.FilePath)
            .Where(predicate ?? (_ => true))
            .ToList();

        var updateLines = metadataCommands
            .Where(command => {
                string metadata = getMetadata(command);
                if (metadata.IsNullOrEmpty()) {
                    return false;
                }

                if (command.Args.Length > 0 && command.Args[0] == metadata) {
                    return false;
                }

                // Sync-check reporting
                if (command.Is("FileTime") || command.Is("ChapterTime") || command.Is("MidwayFileTime") || command.Is("MidwayChapterTime")) {
                    SyncChecker.ReportWrongTime(command.FilePath, command.FileLine, command.Args.Length > 0 ? command.Args[0] : string.Empty, metadata);
                }

                return true;
            })
            .ToDictionary(command => command.StudioLine, command => $"{command.Attribute.Name}{(command.Attribute.Name.EndsWith(":")?"":":")} {getMetadata(command)}");

        if (updateLines.IsEmpty()) {
            return;
        }

        string[] allLines = File.ReadAllLines(tasFilePath);
        int allLinesLength = allLines.Length;
        foreach ((int lineNumber, string replacement) in updateLines) {
            if (lineNumber >= 0 && lineNumber < allLinesLength) {
                allLines[lineNumber] = replacement;
            }
        }

        // Prevent a reload from being triggered by the file-system change
        bool needsReload = Manager.Controller.NeedsReload;
        try {
            File.WriteAllLines(tasFilePath, allLines);
        } catch (IOException) {
            // Something is blocking the TAS file. Just ignore it, the change should be reflected in Studio either way.
        }
        Manager.Controller.NeedsReload = needsReload;

        CommunicationWrapper.SendUpdateLines(updateLines);
    }
    
    public static void UpdateRecordCount(InputController inputController) {
        UpdateAllMetadata(
            "RecordCount",
            command => (int.Parse(command.Args.FirstOrDefault() ?? "0") + 1).ToString(),
            command => int.TryParse(command.Args.FirstOrDefault() ?? "0", out _));
    }

    [DisableRun]
    private static void Disable() {
        damageSectionStart = null;
    }
}

static class PostureSystemExtensions {
    public static float TotalHealth(this PostureSystem postureSystem) => postureSystem.PostureValue + postureSystem.InternalInjury;
}
