using System;
using System.Collections.Generic;
using StudioCommunication;

namespace TAS.Input.Commands;

/// Set hornet's silk.
/// `SetSilk, 9`
/// `SetSilk, +3`
/// `SetSilk, max`
public static class SetSilkCommand {
    private class SetSilkMeta : ITasCommandMeta {
        public string Insert => $"SetSilk{CommandInfo.Separator}[0;max]";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length > 1) {
                yield break;
            }

            yield return new CommandAutoCompleteEntry { Name = "max", IsDone = true, HasNext = false };
            for (var i = 0; i <= 9; i++) {
                yield return new CommandAutoCompleteEntry { Name = i.ToString(), IsDone = true, HasNext = false };
            }
        }
    }

    [TasCommand("SetSilk", LegalInFullGame = false, MetaDataProvider = typeof(SetSilkMeta))]
    private static void SetSilk(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (commandLine.Arguments.Length != 1) {
            AbortTas($"Invalid number of arguments in SetSilk command: '{commandLine.OriginalText}'.");
            return;
        }

        var hero = HeroController.SilentInstance;
        if (hero == null) {
            AbortTas("Attempted to use SetSilk outside of a level");
            return;
        }

        var arg = commandLine.Arguments[0];
        if (arg.Equals("max", StringComparison.OrdinalIgnoreCase)) {
            hero.RefillSilkToMax();
            return;
        }

        if (!int.TryParse(arg, out var value)) {
            AbortTas($"Not a valid number: '{arg}'.");
            return;
        }

        if (value > 0 && arg[0] == '+') {
            hero.AddSilk(value, heroEffect: false);
        } else if (value < 0) {
            hero.TakeSilk(-value);
        } else if (arg[0] != '+') {
            switch (value - hero.playerData.silk) {
                case > 0:
                    hero.AddSilk(value - hero.playerData.silk, heroEffect: false);
                    break;
                case < 0:
                    hero.TakeSilk(hero.playerData.silk - value);
                    break;
            }
        }
    }
}
