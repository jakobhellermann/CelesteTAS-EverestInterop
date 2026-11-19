using System;
using HutongGames.PlayMaker;
using TAS.Utils;

namespace TAS.InfoHUD;

/// Indexes PlayMaker FSMs by variable name, e.g. `HeroController.sprintFSM[High Wall]`, resolving to the
/// NamedVariable itself so it prints its value and can be set through `.Value`
internal class FsmVariableQueryHandler : TargetQuery.Handler {
    private static bool IsFsmIndex(Type type, int memberIdx, string[] memberArgs) =>
        memberArgs[memberIdx].StartsWith(CollectionQueryHandler.IndexKey)
        && (type.IsSameOrSubclassOf(typeof(PlayMakerFSM)) || type.IsSameOrSubclassOf(typeof(Fsm)));

    public override Result<bool, TargetQuery.MemberAccessError> ResolveTargetTypes(out Type[] targetTypes, Type type, ref int memberIdx, string[] memberArgs) {
        if (!IsFsmIndex(type, memberIdx, memberArgs)) {
            targetTypes = null!;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        targetTypes = [typeof(NamedVariable)];
        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
    }

    public override Result<bool, TargetQuery.MemberAccessError> ResolveMember(object? instance, out object? value, Type type, int memberIdx, string[] memberArgs) {
        if (!IsFsmIndex(type, memberIdx, memberArgs)) {
            value = null;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        var fsm = instance switch {
            PlayMakerFSM playMakerFsm => playMakerFsm.Fsm,
            Fsm plainFsm => plainFsm,
            _ => throw new InvalidOperationException($"Unexpected FSM instance '{instance}'"),
        };
        string name = memberArgs[memberIdx][CollectionQueryHandler.IndexKey.Length..];

        value = fsm.Variables.FindVariable(name);
        if (value == null) {
            return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"FSM '{fsm.Name}' has no variable '{name}'"));
        }
        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
    }
}
