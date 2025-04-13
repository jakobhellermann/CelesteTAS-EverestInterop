using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StudioCommunication;

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

    public static readonly ReadOnlyDictionary<char, Actions> DashOnlyChars = new(
        new Dictionary<char, Actions> {
            // {'L', Actions.LeftDashOnly},
            // {'R', Actions.RightDashOnly},
            // {'U', Actions.UpDashOnly},
            // {'D', Actions.DownDashOnly},
        });

    public static readonly ReadOnlyDictionary<char, Actions> MoveOnlyChars = new(
        new Dictionary<char, Actions> {
            // {'L', Actions.LeftMoveOnly},
            // {'R', Actions.RightMoveOnly},
            // {'U', Actions.UpMoveOnly},
            // {'D', Actions.DownMoveOnly},
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

    public static Actions ToggleAction(this Actions actions, Actions other, bool removeMutuallyExclusive) {
        if (actions.HasFlag(other))
            return actions & ~other;

        if (!removeMutuallyExclusive)
            return actions | other;

        // Replace mutually exclusive inputs
        return other switch {
            Actions.Left or Actions.Right => (actions & ~(Actions.Left | Actions.Right)) | other,
            Actions.Up or Actions.Down => (actions & ~(Actions.Up | Actions.Down)) | other,
            // Actions.Feather => (actions & ~(Actions.Up | Actions.Down | Actions.Left | Actions.Right)) | other,
            // Actions.Jump or Actions.Jump2 => (actions & ~(Actions.Jump | Actions.Jump2)) | other,
            // Actions.Grab or Actions.Grab2 => (actions & ~(Actions.Grab | Actions.Grab2)) | other,
            // Actions.Dash or Actions.Dash2 or Actions.DemoDash or Actions.DemoDash2 => (actions & ~(Actions.Dash | Actions.Dash2 | Actions.DemoDash | Actions.DemoDash2)) | other,
            // Actions.LeftDashOnly or Actions.RightDashOnly => (actions & ~(Actions.LeftDashOnly | Actions.RightDashOnly)) | other,
            // Actions.UpDashOnly or Actions.DownDashOnly => (actions & ~(Actions.UpDashOnly | Actions.DownDashOnly)) | other,
            // Actions.LeftMoveOnly or Actions.RightMoveOnly => (actions & ~(Actions.LeftMoveOnly | Actions.RightMoveOnly)) | other,
            // Actions.UpMoveOnly or Actions.DownMoveOnly => (actions & ~(Actions.UpMoveOnly | Actions.DownMoveOnly)) | other,
            _ => actions | other,
        };
    }

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

    public static IEnumerable<Actions> GetDashOnly(this Actions actions) => new Actions[] {
        // Actions.LeftDashOnly,
        // Actions.RightDashOnly,
        // Actions.UpDashOnly,
        // Actions.DownDashOnly,
    }.Where(e => actions.HasFlag(e));

    public static IEnumerable<Actions> GetMoveOnly(this Actions actions) => new Actions[] {
        // Actions.LeftMoveOnly,
        // Actions.RightMoveOnly,
        // Actions.UpMoveOnly,
        // Actions.DownMoveOnly,
    }.Where(e => actions.HasFlag(e));


    public static Actions ToDashOnlyActions(this Actions actions) {
        return actions switch {
            // Actions.Left => Actions.LeftDashOnly,
            // Actions.Right => Actions.RightDashOnly,
            // Actions.Up => Actions.UpDashOnly,
            // Actions.Down => Actions.DownDashOnly,
            _ => actions
        };
    }

    public static Actions ToMoveOnlyActions(this Actions actions) {
        return actions switch {
            // Actions.Left => Actions.LeftMoveOnly,
            // Actions.Right => Actions.RightMoveOnly,
            // Actions.Up => Actions.UpMoveOnly,
            // Actions.Down => Actions.DownMoveOnly,
            _ => actions
        };
    }
}
