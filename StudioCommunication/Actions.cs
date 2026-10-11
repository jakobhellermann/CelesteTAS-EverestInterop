using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StudioCommunication;

public abstract record ActionData {
    public sealed record Regular : ActionData;

    public sealed record Degree : ActionData;

    public sealed record CustomKey : ActionData;

    public sealed record KeyModifier(Actions AppliesTo, Actions DerivedGroup) : ActionData;                   
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
    Attack = 1 << 6,
    
    Inventory = 1 << 7,
    Confirm = 1 << 8,
    
    PressedKey = 1 << 9,

    SuperDash = 1 << 10,
    Pause = 1 << 11,
}

public static class ActionsUtils {
    private static readonly ActionData.Regular RegularData = new();
    private static readonly ActionData.Degree DegreeData = new();
    private static readonly ActionData.CustomKey CustomKeyData = new();

    public static ActionData Data(this Actions actions) => actions switch {
        Actions.PressedKey => CustomKeyData,
        _ => RegularData,
    };

    /// The modifier action whose derived group contains this action; None if the action isn't derived
    public static Actions ModifierOf(this Actions action) {
        return Actions.None;
    }

    /// Translate an action into its derived variant within the modifier's group; unchanged if not applicable
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

    /// Amount of actions which are set within the given group
    public static int CountIn(this Actions actions, Actions group) {
        int count = 0;
        uint remaining = (uint) (actions & group);
        while (remaining != 0) {
            remaining &= remaining - 1;
            count++;
        }
        return count;
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
        (Actions.Up, Actions.Down),
    ];

    public static readonly ReadOnlyDictionary<char, Actions> Chars = new(
        new Dictionary<char, Actions> {
            {'L', Actions.Left},
            {'R', Actions.Right},
            {'U', Actions.Up},
            {'D', Actions.Down},
            
            {'J', Actions.Jump},
            {'C', Actions.Dash},
            {'X', Actions.Attack},
            {'S', Actions.SuperDash},
            
            {'I', Actions.Inventory},
            {'O', Actions.Confirm},
            {'E', Actions.Pause},
            
            {'P', Actions.PressedKey},
        });

    public static bool TryParse(char c, out Actions actions) {
        return Chars.TryGetValue(c, out actions);
    }

    public static Actions ActionForChar(this char c) =>
        c.ToString().ToUpper()[0] switch {
            'R' => Actions.Right,
            'L' => Actions.Left,
            'U' => Actions.Up,
            'D' => Actions.Down,
            
            'J' => Actions.Jump,
            'C' => Actions.Dash,
            'X' => Actions.Attack,
            'S' => Actions.SuperDash,
            
            'I' => Actions.Inventory,
            'O' => Actions.Confirm,
            'E' => Actions.Pause,
            
            'P' => Actions.PressedKey,
            
            _ => Actions.None,
        };

    public static char CharForAction(this Actions actions) =>
        actions switch {
            Actions.Right => 'R',
            Actions.Left => 'L',
            Actions.Up => 'U',
            Actions.Down => 'D',
            
            Actions.Jump => 'J',
            Actions.Dash => 'C',
            Actions.Attack => 'X',
            Actions.SuperDash => 'S',
            
            Actions.Inventory => 'I',
            Actions.Confirm => 'O',
            Actions.Pause => 'E',
            
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
        Actions.Attack,
        Actions.SuperDash,
        
        Actions.Inventory,
        Actions.Confirm,
        Actions.Pause,
        
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
