namespace TAS.Communication;

public static class GameData {
    public static string GetConsoleCommand(bool simple) {
        if (Player.i is not { } player) return "";
        var sceneName = GameCore.Instance.gameLevel.SceneName;

        var pos = player.transform.position;
        return $"load {sceneName} {pos.x:0.00} {pos.y:0.00}";
    }
}