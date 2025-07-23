﻿using System;
using System.Collections.Generic;
using System.Linq;
using StudioCommunication;
using StudioCommunication.Util;
using TAS.InfoHUD;
using TAS.Playback;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class InvokeCommand {
    private class InvokeMeta : ITasCommandMeta {
        public string Insert => $"Invoke{CommandInfo.Separator}[0;Query]{CommandInfo.Separator}[1;Parameters]";
        public bool HasArguments => true;

        public int GetHash(string[] args, string filePath, int fileLine) {
            var hash = new HashCode();
            hash.Add(SetCommand.SetMeta.GetQueryArgs(args, 0).Aggregate(new HashCode(), (argHash, arg) => argHash.Append(arg.GetStableHashCode())).ToHashCode());
            for (int i = 1; i < args.Length; i++) {
                hash.Add(SetCommand.SetMeta.GetQueryArgs(args, i).Aggregate(new HashCode(), (argHash, arg) => argHash.Append(arg.GetStableHashCode())).ToHashCode());
            }
            hash.Add(args.Length);
            return hash.ToHashCode();
        }

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            // Target
            string[] targetQueryArgs = SetCommand.SetMeta.GetQueryArgs(args, 0).ToArray();
            if (args.Length <= 1) {

                using var enumerator = TargetQuery.ResolveAutoCompleteEntries(targetQueryArgs, TargetQuery.Variant.Invoke);
                while (enumerator.MoveNext()) {
                    yield return enumerator.Current with { HasNext = true };
                }
                yield break;
            }

            // Parameters
            string[] paramQueryArgs = SetCommand.SetMeta.GetQueryArgs(args, 1).ToArray();
            var baseTypes = TargetQuery.ResolveBaseTypes(targetQueryArgs, out string[] memberArgs);
            var targetTypes = baseTypes
                .Select(type => TargetQuery.RecurseMemberType(type, memberArgs, TargetQuery.Variant.Invoke))
                .Where(type => type != null)
                .ToArray();

            for (int i = 1; i < Math.Min(args.Length, targetTypes.Length + 1); i++) {
                using var enumerator = TargetQuery.ResolveAutoCompleteEntries(paramQueryArgs, TargetQuery.Variant.Get, [targetTypes[i - 1]!]);
                while (enumerator.MoveNext()) {
                    yield return enumerator.Current with { HasNext = i < targetTypes.Length };
                }
            }
        }
    }

    private static (string Name, int Line)? activeFile;

    private static void ReportError(string message) {
        if (activeFile == null) {
            Log.Toast($"Invoke Command Failed: {message}");
        } else {
            PopupToast.ShowAndLog($"""
                                   Invoke '{activeFile.Value.Name}' line {activeFile.Value.Line} failed:
                                   {message}
                                   """);
        }
    }

    // Invoke, Level.Method, Parameters...
    // Invoke, Session.Method, Parameters...
    // Invoke, Entity.Method, Parameters...
    // Invoke, Type.StaticMethod, Parameters...
    [TasCommand("Invoke", LegalInFullGame = false, MetaDataProvider = typeof(InvokeMeta))]
    private static void Invoke(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        activeFile = (filePath, fileLine);
        Invoke(commandLine.Arguments);
        activeFile = null;
    }

    private static void Invoke(string[] args) {
        if (args.Length < 1) {
            ReportError("Target-query required");
            return;
        }

        var result = TargetQuery.InvokeMemberMethods(args[0], args[1..]);
        if (result.Failure) {
            ReportError(result.Error.ToString());
        }
    }
}
