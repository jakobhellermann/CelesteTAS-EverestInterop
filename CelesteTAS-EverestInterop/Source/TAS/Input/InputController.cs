using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste.Mod;
using JetBrains.Annotations;
using TAS.Input.Commands;
using TAS.Utils;

namespace TAS.Input;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class ClearInputsAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class ParseFileEndAttribute : Attribute;

#nullable enable

/// Manages inputs, commands, etc. for the current TAS file
public class InputController {
    static InputController() {
        AttributeUtils.CollectMethods<ClearInputsAttribute>();
        AttributeUtils.CollectMethods<ParseFileEndAttribute>();
    }

    private static readonly Dictionary<string, FileSystemWatcher> watchers = new();

    public readonly List<InputFrame> Inputs = [];
    public readonly SortedDictionary<int, List<Command>> Commands = new();
    public readonly SortedDictionary<int, FastForward> FastForwards = new();
    public readonly SortedDictionary<int, FastForward> FastForwardLabels = new();

    public InputFrame? Previous => Inputs!.GetValueOrDefault(CurrentFrameInTAS - 1);
    public InputFrame Current => Inputs!.GetValueOrDefault(CurrentFrameInTAS)!;
    public InputFrame? Next => Inputs!.GetValueOrDefault(CurrentFrameInTAS + 1);

    public int CurrentFrameInTAS { get; private set; } = 0;
    public int CurrentFrameInInput { get; private set; } = 0;
    public int CurrentParsingFrame => Inputs.Count;

    public List<Command> CurrentCommands => Commands.GetValueOrDefault(CurrentFrameInTAS) ?? [];

    public FastForward? CurrentFastForward => NextLabelFastForward ??
                                               FastForwards.FirstOrDefault(pair => pair.Key > CurrentFrameInTAS).Value ??
                                               FastForwards.LastOrDefault().Value;
    public bool HasFastForward => CurrentFastForward is { } forward && forward.Frame > CurrentFrameInTAS;

    public FastForward? NextLabelFastForward;

    /// Indicates whether the current TAS file needs to be re-parsed before running
    private bool needsReload = true;

    /// All files involved in the current TAS
    private readonly HashSet<string> usedFiles = [];

    private const int InvalidChecksum = -1;
    private int checksum = InvalidChecksum;

    /// Current checksum of the TAS, used to increment RecordCount
    public int Checksum => checksum == InvalidChecksum ? checksum = CalcChecksum(Inputs.Count - 1) : checksum;

    /// Whether the controller can be advanced to a next frame
    public bool CanPlayback => CurrentFrameInTAS < Inputs.Count;

    /// Whether the TAS should be paused on this frame
    public bool Break => CurrentFastForward?.Frame == CurrentFrameInTAS;

    private static readonly string DefaultFilePath = Path.Combine(Everest.PathEverest, "Celeste.tas");

    private string filePath = string.Empty;
    public string FilePath {
        get {
            var path = !string.IsNullOrEmpty(filePath) ? filePath : DefaultFilePath;

            // Ensure path exists
            if (!File.Exists(path)) {
                File.WriteAllText(path, string.Empty);
            }
            return path;
        }
        set {
            if (filePath == value) {
                return;
            }
            if (string.IsNullOrWhiteSpace(value)) {
                filePath = string.Empty;
                return;
            }

            filePath = Path.GetFullPath(value);
            if (!File.Exists(filePath)) {
                filePath = DefaultFilePath;
            }

            if (Manager.Running) {
                Manager.DisableRunLater();
            }

            // Preload the TAS file
            Stop();
            Clear();
            RefreshInputs();
        }
    }

    /// Re-parses the TAS file if necessary
    public void RefreshInputs(bool forceRefresh = false) {
        if (!needsReload && !forceRefresh) {
            return; // Already up-to-date
        }

        "Refreshing inputs...".Log(LogLevel.Debug);

        int lastChecksum = Checksum;
        bool firstRun = usedFiles.IsEmpty();

        Clear();
        if (ReadFile(FilePath)) {
            if (Manager.NextState == Manager.State.Disabled) {
                // The TAS contains something invalid
                Clear();
                Manager.DisableRun();
            } else {
                needsReload = false;
                StartWatchers();
                AttributeUtils.Invoke<ParseFileEndAttribute>();

                if (!firstRun && lastChecksum != Checksum) {
                    MetadataCommands.UpdateRecordCount(this);
                }
            }
        } else {
            // Something failed while trying to parse
            Clear();
        }

        CurrentFrameInTAS = Math.Min(Inputs.Count, CurrentFrameInTAS);
    }

    /// Moves the controller 1 frame forward, updating inputs and triggering commands
    public void AdvanceFrame() {
        foreach (var command in CurrentCommands) {
            if (command.Attribute.ExecuteTiming.Has(ExecuteTiming.Runtime) &&
                (!EnforceLegalCommand.EnabledWhenRunning || command.Attribute.LegalInFullGame))
            {
                command.Invoke();
            }

            // SaveAndQuitReenter inserts inputs, so we can't continue executing the commands
            // It already handles the moving of all following commands
            if (command.Attribute.Name == "SaveAndQuitReenter") break;
        }

        if (!CanPlayback) {
            return;
        }

        ExportGameInfo.ExportInfo();
        StunPauseCommand.UpdateSimulateSkipInput();
        InputHelper.FeedInputs(Current);

        // Increment if it's still the same input
        if (CurrentFrameInInput == 0 || Current.Line == Previous!.Line && Current.RepeatIndex == Previous.RepeatIndex && Current.FrameOffset == Previous.FrameOffset) {
            CurrentFrameInInput++;
        } else {
            CurrentFrameInInput = 1;
        }

        CurrentFrameInTAS++;
    }

    /// Parses the file and adds the inputs / commands to the TAS
    public bool ReadFile(string path, int startLine = 0, int endLine = int.MaxValue, int studioLine = 0, int repeatIndex = 0, int repeatCount = 0) {
        try {
            if (!File.Exists(path)) {
                return false;
            }

            usedFiles.Add(path);
            ReadLines(File.ReadLines(path).Take(endLine), path, startLine, studioLine, repeatIndex, repeatCount);

            return true;
        } catch (Exception e) {
            e.Log(LogLevel.Error);
            return false;
        }
    }

    /// Parses the lines and adds the inputs / commands to the TAS
    public void ReadLines(IEnumerable<string> lines, string path, int startLine, int studioLine, int repeatIndex, int repeatCount, bool lockStudioLine = false) {
        int fileLine = 0;
        foreach (string readLine in lines) {
            fileLine++;
            if (fileLine < startLine) {
                continue;
            }

            if (!ReadLine(readLine, path, fileLine, studioLine, repeatIndex, repeatCount)) {
                return;
            }

            if (path == FilePath && !lockStudioLine) {
                studioLine++;
            }
        }

        // Add a hidden label at the of the text block
        if (path == FilePath) {
            FastForwardLabels[CurrentParsingFrame] = new FastForward(CurrentParsingFrame, "", studioLine);
        }
    }

    /// Parses the line and adds the inputs / commands to the TAS
    public bool ReadLine(string line, string path, int fileLine, int studioLine, int repeatIndex = 0, int repeatCount = 0) {
        string lineText = line.Trim();

        if (Command.TryParse(path, fileLine, lineText, CurrentParsingFrame, studioLine, out Command command)) {
            if (!Commands.TryGetValue(CurrentParsingFrame, out var commands)) {
                Commands[CurrentParsingFrame] = commands = new List<Command>();
            }
            commands.Add(command);

            if (command.Is("Play")) {
                // Workaround for the 'Play' command:
                // It needs to stop reading the current file when it's done to prevent recursion
                return false;
            }
        } else if (lineText.StartsWith("***")) {
            var fastForward = new FastForward(CurrentParsingFrame, lineText.Substring("***".Length), studioLine);
            if (FastForwards.TryGetValue(CurrentParsingFrame, out var oldFastForward) && oldFastForward.SaveState && !fastForward.SaveState) {
                // ignore
            } else {
                FastForwards[CurrentParsingFrame] = fastForward;
            }
        } else if (lineText.StartsWith("#")) {
            // A label need to start with a # and immediately follow with the text
            if (lineText.Length >= 2 && lineText[0] == '#' && char.IsLetter(lineText[1])) {
                FastForwardLabels[CurrentParsingFrame] = new FastForward(CurrentParsingFrame, "", studioLine);
            }

            // if (!Comments.TryGetValue(path, out var comments)) {
            //     Comments[path] = comments = new List<Comment>();
            // }
            // comments.Add(new Comment(path, CurrentParsingFrame, subLine, lineText));
        } else if (!AutoInputCommand.TryInsert(path, lineText, studioLine, repeatIndex, repeatCount)) {
            AddFrames(lineText, studioLine, repeatIndex, repeatCount);
        }

        return true;
    }

    /// Parses the input line and adds it to the TAS
    public void AddFrames(string line, int studioLine, int repeatIndex = 0, int repeatCount = 0, int frameOffset = 0) {
        if (InputFrame.TryParse(line, studioLine, Inputs.LastOrDefault(), out var inputFrame, repeatIndex, repeatCount, frameOffset)) {
            AddFrames(inputFrame);
        }
    }

    /// Adds the inputs to the TAS
    public void AddFrames(InputFrame inputFrame) {
        for (int i = 0; i < inputFrame.Frames; i++) {
            Inputs.Add(inputFrame);
        }

        LibTasHelper.WriteLibTasFrame(inputFrame);
    }

    /// Fast-forwards to the next label / breakpoint
    public void FastForwardToNextLabel() {
        NextLabelFastForward = null;
        RefreshInputs();

        var next = FastForwardLabels.FirstOrDefault(pair => pair.Key > CurrentFrameInTAS).Value;
        if (next != null && HasFastForward && CurrentFastForward is { } last && next.Frame > last.Frame) {
            // Forward to another breakpoint in-between instead
            NextLabelFastForward = last;
        } else {
            NextLabelFastForward = next;
        }

        Manager.NextState = Manager.State.Running;
    }

    /// Stops execution of the current TAS and resets state
    public void Stop() {
        CurrentFrameInTAS = 0;
        CurrentFrameInInput = 0;
        NextLabelFastForward = null;
    }

    /// Clears all parsed data for the current TAS
    public void Clear() {
        Inputs.Clear();
        Commands.Clear();
        FastForwards.Clear();
        FastForwardLabels.Clear();

        foreach (var watcher in watchers.Values) {
            watcher.Dispose();
        }
        watchers.Clear();
        usedFiles.Clear();

        checksum = InvalidChecksum;
        needsReload = true;
    }

    /// Create file-system-watchers for all TAS-files used, to detect changes
    private void StartWatchers() {
        foreach (var path in usedFiles) {
            string fullPath = Path.GetFullPath(path);

            // Watch TAS file
            CreateWatcher(fullPath);
        }

        void CreateWatcher(string path) {
            if (watchers.ContainsKey(path)) {
                return;
            }

            var watcher = new FileSystemWatcher();
            watcher.Path = Path.GetDirectoryName(path)!;
            watcher.Filter = Path.GetFileName(path);

            watcher.Changed += OnTasFileChanged;
            watcher.Created += OnTasFileChanged;
            watcher.Deleted += OnTasFileChanged;
            watcher.Renamed += OnTasFileChanged;

            try {
                watcher.EnableRaisingEvents = true;
            } catch (Exception e) {
                e.LogException($"Failed watching folder: {watcher.Path}, filter: {watcher.Filter}");
                watcher.Dispose();
                return;
            }

            watchers[path] = watcher;
        }

        void OnTasFileChanged(object sender, FileSystemEventArgs e) {
            needsReload = true;
        }
    }

    /// Calculate a checksum until the specified frame
    private int CalcChecksum(int upToFrame) {
        var hash = new HashCode();
        hash.Add(filePath);

        for (int i = 0; i < upToFrame; i++) {
            hash.Add(Inputs[i]);

            if (Commands.GetValueOrDefault(i) is { } commands) {
                foreach (var command in commands.Where(command => command.Attribute.CalcChecksum)) {
                    hash.Add(command.LineText);
                }
            }
        }

        return hash.ToHashCode();
    }

    public InputController Clone() {
        InputController clone = new();

        clone.Inputs.AddRange(Inputs);

        foreach ((int line, var fastForward) in FastForwards) {
            clone.FastForwards.Add(line, fastForward);
        }
        foreach ((int line, var fastForward) in FastForwardLabels) {
            clone.FastForwardLabels.Add(line, fastForward);
        }

        // foreach (string filePath in Comments.Keys) {
        //     clone.Comments[filePath] = new List<Comment>(Comments[filePath]);
        // }

        foreach (int frame in Commands.Keys) {
            clone.Commands[frame] = [..Commands[frame]];
        }

        clone.needsReload = needsReload;
        foreach (var file in usedFiles) {
            clone.usedFiles.Add(file);
        }
        clone.CurrentFrameInTAS = CurrentFrameInTAS;
        clone.CurrentFrameInInput = CurrentFrameInInput;
        // clone.CurrentFrameInInputForHud = CurrentFrameInInputForHud;
        // clone.SavestateChecksum = clone.CalcChecksum(CurrentFrameInTas);

        clone.checksum = checksum;
        // clone.initializationFrameCount = initializationFrameCount;

        return clone;
    }

    public void CopyProgressFrom(InputController other) {
        CurrentFrameInTAS = other.CurrentFrameInTAS;
        CurrentFrameInInput = other.CurrentFrameInInput;
    }
}
