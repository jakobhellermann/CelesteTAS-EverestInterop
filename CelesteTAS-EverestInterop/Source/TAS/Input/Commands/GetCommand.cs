using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using StudioCommunication;
using StudioCommunication.Util;
using TAS.Communication;
using TAS.InfoHUD;
using TAS.Playback;

namespace TAS.Input.Commands;

/// Reads a target-query value while the TAS is playing and writes the current value back into the file.
///   Get, HeroController.transform.position
/// becomes, once the command is reached during playback:
///   Get, HeroController.transform.position, (30.5, 12.0, 0.0)
public static class GetCommand {
    public const string CommandName = "Get";
    
    private class GetMeta : ITasCommandMeta {
        public string Insert => $"{CommandName}{CommandInfo.Separator}[0;Query]";
        public bool HasArguments => true;

        public int GetHash(string[] args, string filePath, int fileLine) {
            return TargetQuery.GetQueryArgs(args, 0).Aggregate(17, (current, arg) => 31 * current + 17 * arg.GetStableHashCode());
        }

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            // Only auto-complete the query argument
            if (args.Length > 1) {
                yield break;
            }

            string[] queryArgs = args.Length > 0 ? args[0].Split('.') : [];
            using var enumerator = TargetQuery.ResolveAutoCompleteEntries(queryArgs, TargetQuery.Variant.Get);
            while (enumerator.MoveNext()) {
                yield return enumerator.Current;
            }
        }
    }

    // Get, Query
    [TasCommand(CommandName, CalcChecksum = false, MetaDataProvider = typeof(GetMeta))]
    private static void Get(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0])) {
            ReportError(filePath, fileLine, "Expected target-query");
            return;
        }

        string query = args[0];
        var result = TargetQuery.GetMemberValues(query);
        if (result.Failure) {
            ReportError(filePath, fileLine, result.Error.ToString());
            return;
        }

        string line = result.Value.Count > 0
            ? $"{CommandName}, {query}, {string.Join(", ", result.Value.Select(entry => FormatValue(entry.Value)))}"
            : $"{CommandName}, {query}";
        WriteValue(filePath, fileLine, studioLine, line);
    }

    private static string FormatValue(object? value) => value switch {
        null => "null",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    /// Replaces the command's line in its file with the new text, keeping Studio in sync
    private static void WriteValue(string filePath, int fileLine, int studioLine, string newLineText) {
        string[] allLines;
        try {
            allLines = File.ReadAllLines(filePath);
        } catch (IOException) {
            return;
        }

        // fileLine is 1-based
        int lineIndex = fileLine - 1;
        if (lineIndex < 0 || lineIndex >= allLines.Length || allLines[lineIndex] == newLineText) {
            return;
        }
        allLines[lineIndex] = newLineText;

        File.WriteAllText(filePath, allLines.FormatTasLinesToText());

        // The studio line only addresses the currently open (main) file
        // TODO remove this?
        if (filePath == Manager.Controller.FilePath) {
            CommunicationWrapper.SendUpdateLines(new Dictionary<int, string> { { studioLine, newLineText } });
        }
    }

    private static void ReportError(string filePath, int fileLine, string message) {
        PopupToast.ShowAndLog($"""
                               Get '{Path.GetFileName(filePath)}' line {fileLine} failed:
                               {message}
                               """);
    }
}
