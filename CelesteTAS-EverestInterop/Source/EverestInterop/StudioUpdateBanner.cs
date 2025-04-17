using System;
using BepInEx.Logging;

namespace TAS.EverestInterop;

/// Announces Studio download / install / update progress to the player.
/// The upstream Celeste mod renders an in-game HUD banner; BepInEx hosts don't have that renderer, so state
/// changes are surfaced as game toasts (NineSolsAPI ToastManager) plus log output instead.
internal static class StudioUpdateBanner {
    public enum State {
        Inactive, Download, Install, Success, Failure, Launch
    }

    private static State currentState = State.Inactive;

    public static State CurrentState {
        get => currentState;
        set {
            if (currentState == value) {
                return;
            }

            currentState = value;

            if (value == State.Inactive) {
                return;
            }

            string text = value switch {
                State.Download => "Downloading Studio...",
                State.Install => "Installing Studio...",
                State.Success => "Installed Studio update",
                State.Failure => "Failed to install Studio (see error_report.txt)",
                State.Launch => "Launching Studio...",
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
            };
            text.Log();
            GameToasts.Show(text);
        }
    }
}
