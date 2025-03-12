using Cysharp.Threading.Tasks;
using TAS.Utils;

namespace EverestInterop;

public static class UniTaskHelper {
    private static object PlayerLoopRunner(PlayerLoopTiming timing) {
        var runners = (object[])typeof(PlayerLoopHelper).GetFieldInfo("runners")!.GetValue(null);
        return runners[(int)timing];
    }

    public static IPlayerLoopItem?[] GetLoopItems(PlayerLoopTiming timing) =>
        PlayerLoopRunner(timing).GetFieldValue<IPlayerLoopItem?[]>("loopItems")!;

    public static int Clear(PlayerLoopTiming timing) => PlayerLoopRunner(timing).InvokeMethod<int>("Clear");
}
