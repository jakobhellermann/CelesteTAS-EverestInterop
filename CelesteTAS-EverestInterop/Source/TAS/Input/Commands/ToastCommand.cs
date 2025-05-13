using NineSolsAPI;
using System;
using System.Linq;
using StudioCommunication;
using StudioCommunication.Util;
using System.Collections.Generic;
using TAS.InfoHUD;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class ToastCommand {
    internal class ToastMeta : ITasCommandMeta {
        public string Insert => $"Toast{CommandInfo.Separator}[0;Query]";
        public bool HasArguments => true;

        public int GetHash(string[] args, string filePath, int fileLine) {
            var hash = new HashCode();
            hash.Add(GetQueryArgs(args, 0).Aggregate(new HashCode(), (argHash, arg) => argHash.Append(arg.GetStableHashCode())).ToHashCode());
            hash.Add(GetQueryArgs(args, 1).Aggregate(new HashCode(), (argHash, arg) => argHash.Append(arg.GetStableHashCode())).ToHashCode());
            hash.Add(args.Length);
            return hash.ToHashCode();
        }

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            // Target
            string[] targetQueryArgs = GetQueryArgs(args, 0).ToArray();
            if (args.Length <= 1) {

                using var enumerator = TargetQuery.ResolveAutoCompleteEntries(targetQueryArgs, TargetQuery.Variant.Set);
                while (enumerator.MoveNext()) {
                    yield return enumerator.Current with { HasNext = false };
                }
                yield break;
            }

            // Parameter
            {
                string[] paramQueryArgs = GetQueryArgs(args, 1).ToArray();
                var baseTypes = TargetQuery.ResolveBaseTypes(targetQueryArgs, out string[] memberArgs);
                var targetTypes = baseTypes
                    .Select(type => TargetQuery.RecurseMemberType(type, memberArgs, TargetQuery.Variant.Set))
                    .Where(type => type != null)
                    .ToArray();

                using var enumerator = TargetQuery.ResolveAutoCompleteEntries(paramQueryArgs, TargetQuery.Variant.Get, targetTypes!);
                while (enumerator.MoveNext()) {
                    yield return enumerator.Current;
                }
            }
        }

        internal static IEnumerable<string> GetQueryArgs(string[] args, int index) {
            if (args.Length <= index) {
                return [];
            }

            return args[index]
                .Split('.')
                // Only skip last part if we're currently editing that
                .SkipLast(args.Length == index + 1 ? 1 : 0);
        }
    }

    private static (string Name, int Line)? activeFile;

    private static void ReportError(string message) {
        if (activeFile == null) {
            Log.Toast($"Set Command Failed: {message}");
        } else {
            Log.Toast($"""
                              Set '{activeFile.Value.Name}' line {activeFile.Value.Line} failed:
                              {message}
                              """);
        }
    }

    [TasCommand("Toast", LegalInFullGame = false, MetaDataProvider = typeof(ToastMeta))]
    private static void Toast(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        activeFile = (filePath, fileLine);
        Toast(commandLine.Arguments);
        activeFile = null;
    }

    private static void Toast(string[] args) {
        if (args.Length < 1) {
            ReportError("Target-query required");
            return;
        }

        var result = TargetQuery.GetMemberValues(args[0]);
        if (result.Failure) {
            ReportError(result.Error.ToString());
            return;
        }

        if (result.Value.Count == 0) {
            ToastManager.Toast("No instances found");
        }

        foreach (var (_, value) in result.Value) {
            ToastManager.Toast(value);
        }
    }
}
