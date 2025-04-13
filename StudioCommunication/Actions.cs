using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StudioCommunication;

public abstract record ActionData {
    public sealed record Regular : ActionData;

    public sealed record Degree : ActionData;

    public sealed record CustomKey : ActionData;

    public sealed record KeyModifier(Actions AppliesTo, Actions DerivedGroup) : ActionData {
        public int DerivedCountOf(Actions actions) {
            int count = 0;
            uint remaining = (uint) (actions & DerivedGroup);
            while (remaining != 0) {
                remaining &= remaining - 1;
                count++;
            }
            return count;
        }
    }
}

[Flags]
public enum Actions {
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Up = 1 << 2,
    Down = 1 << 3,
    Jump = 1 << 4,
    Dash = 1 << 5,
    Interact = 1 << 6,
    Attack = 1 << 7,
    Shoot = 1 << 8,
    Parry = 1 << 9,
    Talisman = 1 << 10,
    Nymph = 1 << 11,
    Heal = 1 << 12,
    ArrowNext = 1 << 13,
    ArrowPrev = 1 << 14,
    Map = 1 << 15,

    PressedKey = 1 << 16,
}

public static class ActionsUtils {
    private static readonly ActionData.Regular RegularData = new();
    private static readonly ActionData.CustomKey CustomKeyData = new();

    public static ActionData Data(this Actions actions) => actions switch {
        Actions.PressedKey => CustomKeyData,
        _ => RegularData,
    };

    public static Actions ActionModifierOf(Actions action) => Actions.None;

    public static Actions ToDerived(this Actions action, ActionData.KeyModifier modifier) {
        uint remaining = (uint) modifier.DerivedGroup;
        while (remaining != 0) {
            var derived = (Actions) (remaining & ~remaining + 1);
            remaining &= remaining - 1;

            if (derived.CharForAction() == action.CharForAction())
                return derived;
        }
        return action;
    }

    /// Actions which are set within the given group, ordered by their bit
    public static IEnumerable<Actions> InGroup(this Actions actions, Actions group) {
        uint remaining = (uint) (actions & group);
        while (remaining != 0) {
            yield return (Actions) (remaining & ~remaining + 1);
            remaining &= remaining - 1;
        }
    }


    /// Pairs of actions which can be swapped in-place
    public static readonly (Actions A, Actions B)[] SwappablePairs = [
        (Actions.Left, Actions.Right),
    ];

    public static readonly ReadOnlyDictionary<char, Actions> Chars = new(
        new Dictionary<char, Actions> {
            {'L', Actions.Left},
            {'R', Actions.Right},
            {'U', Actions.Up},
            {'D', Actions.Down},
            {'J', Actions.Jump},
            {'X', Actions.Dash},
            {'E', Actions.Interact},
            {'A', Actions.Attack},
            {'S', Actions.Shoot},
            {'K', Actions.Parry},
            {'T', Actions.Talisman},
            {'N', Actions.Nymph},
            {'H', Actions.Heal},
            // {'X', Actions.ArrowNext},
            // {'X', Actions.ArrowPrev},
            // {'X', Actions.Map},
            {'P', Actions.PressedKey},
        });

    public static bool TryParse(char c, out Actions actions) {
        return Chars.TryGetValue(c, out actions);
    }

    public static Actions ActionForChar(this char c) =>
        c.ToString().ToUpper()[0] switch {
            'L' => Actions.Left,
            'R' => Actions.Right,
            'U' => Actions.Up,
            'D' => Actions.Down,
            'J' => Actions.Jump,
            'X' => Actions.Dash,
            'E' => Actions.Interact,
            'A' => Actions.Attack,
            'S' => Actions.Shoot,
            'K' => Actions.Parry,
            'T' => Actions.Talisman,
            'N' => Actions.Nymph,
            'H' => Actions.Heal,
            'P' => Actions.PressedKey,
            _ => Actions.None,
        };

    public static char CharForAction(this Actions actions) =>
        actions switch {
            Actions.Left => 'L',
            Actions.Right => 'R',
            Actions.Up => 'U',
            Actions.Down => 'D',
            Actions.Jump => 'J',
            Actions.Dash => 'X',
            Actions.Interact => 'E',
            Actions.Attack => 'A',
            Actions.Shoot => 'S',
            Actions.Parry => 'K',
            Actions.Talisman => 'T',
            Actions.Nymph => 'N',
            Actions.Heal => 'H',
            Actions.PressedKey => 'P',
            _ => ' ',
        };

    public static IEnumerable<Actions> Sorted(this Actions actions) => new[] {
        Actions.Left,
        Actions.Right,
        Actions.Up,
        Actions.Down,
        Actions.Jump,
        Actions.Dash,
        Actions.Interact,
        Actions.Attack,
        Actions.Shoot,
        Actions.Parry,
        Actions.Talisman,
        Actions.Nymph,
        Actions.Heal,
        Actions.PressedKey,
    }.Where(e => actions.HasFlag(e));

    public static Actions ToggleAction(this Actions actions, Actions other, bool removeMutuallyExclusive) {
        if (actions.HasFlag(other))
            return actions & ~other;

        if (!removeMutuallyExclusive)
            return actions | other;

        // Replace mutually exclusive inputs
        return other switch {
            Actions.Left or Actions.Right => (actions & ~(Actions.Left | Actions.Right)) | other,
            Actions.Up or Actions.Down => (actions & ~(Actions.Up | Actions.Down)) | other,
            _ => actions | other,
        };
    }
}
