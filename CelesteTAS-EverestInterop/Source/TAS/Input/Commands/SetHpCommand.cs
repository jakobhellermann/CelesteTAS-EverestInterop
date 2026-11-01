using System;
using System.Collections.Generic;
using StudioCommunication;

namespace TAS.Input.Commands;

/// Set hornet's health.
/// `SetHp, 3`
/// `SetHp, +1`
/// `SetHp, max`
public static class SetHpCommand {
    private class SetHpMeta : ITasCommandMeta {
        public string Insert => $"SetHp{CommandInfo.Separator}[0;max]";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length > 1) {
                yield break;
            }

            yield return new CommandAutoCompleteEntry { Name = "max", IsDone = true, HasNext = false };
            for (var i = 1; i <= 10; i++) {
                yield return new CommandAutoCompleteEntry { Name = i.ToString(), IsDone = true, HasNext = false };
            }
        }
    }

    [TasCommand("SetHp", LegalInFullGame = false, MetaDataProvider = typeof(SetHpMeta))]
    private static void SetHp(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (commandLine.Arguments.Length != 1) {
            AbortTas($"Invalid number of arguments in SetHp command: '{commandLine.OriginalText}'.");
            return;
        }

        var hero = HeroController.SilentInstance;
        if (hero == null) {
            AbortTas("Attempted to use SetHp outside of a level");
            return;
        }

        var arg = commandLine.Arguments[0];
        if (arg.Equals("max", StringComparison.OrdinalIgnoreCase)) {
            hero.RefillHealthToMax();
            return;
        }

        if (!int.TryParse(arg, out var value)) {
            AbortTas($"Not a valid number: '{arg}'.");
            return;
        }

        if (value > 0 && arg[0] == '+') {
            hero.AddHealth(value);
        } else if (value < 0) {
            hero.TakeHealth(-value);
        } else if (arg[0] != '+') {
            switch (value - hero.playerData.health) {
                case > 0:
                    hero.AddHealth(value - hero.playerData.health);
                    break;
                case < 0:
                    hero.TakeHealth(hero.playerData.health - value);
                    break;
            }
        }
    }
}
