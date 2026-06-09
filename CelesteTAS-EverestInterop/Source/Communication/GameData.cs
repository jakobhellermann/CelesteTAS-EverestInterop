using UnityEngine;

namespace TAS.Communication;

public static class GameData {
    public static string GetConsoleCommand(bool simple) {
        var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        var hero = HeroController.UnsafeInstance;
        var pos = hero ? hero.transform.position : Vector3.zero;
        return $"load {sceneName} {pos.x:0.00} {pos.y:0.00}";
    }
}
