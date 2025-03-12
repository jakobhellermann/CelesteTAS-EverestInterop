using System;
using StudioCommunication;
using UnityEngine;

namespace TAS.Input.Commands;

public static class ExitGameCommand {
    [TasCommand("ExitGame")]
    private static void ExitGame(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        SingletonBehaviour<GameCore>.Instance.gameLevel.gameObject.SetActive(false);
        SingletonBehaviour<GameCore>.Instance.gameLevel.SetLevelDestroy();
        SingletonBehaviour<GameCore>.Instance.soundManager.ambienceManager.StopAllAMB();
        Application.Quit();
        Environment.Exit(0);
    }
}
