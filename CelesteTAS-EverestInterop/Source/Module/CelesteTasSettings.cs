using BepInEx.Configuration;
using System;
using TAS.Communication;
using TAS.EverestInterop.Hitboxes;

namespace TAS.Module;

using GameSettings = StudioCommunication.GameSettings;

public class CelesteTasSettings {
    public CelesteTasSettings(ConfigFile config) {
        ShowHitboxes = config.Bind("Hitboxes", "Visible", false);
        ShowHitboxes.SettingChanged += (_, _) => {
            _studioShared.Hitboxes = ShowHitboxes.Value;
            SyncSettings();
        };
        ShowHitboxes.SettingChanged += (_, _) => TasMod.Instance.HitboxModule.Reload();
        HitboxFilter = config.Bind("Hitboxes", "Hitbox Filter", HitboxType.Default);
        HitboxFilter.SettingChanged += (_, _) => ShowHitboxes.Value = true;
        CenterCamera = config.Bind("More options", "Center Camera", false);
        CenterCamera.SettingChanged += (_, _) => {
            _studioShared.CenterCamera = CenterCamera.Value;
            SyncSettings();
        };
    }

    // Settings which are shared / controllable from Studio
    internal GameSettings _studioShared = new();

    internal GameSettings StudioShared {
        get => _studioShared;
        set {
            _studioShared = value;
            updating = true;
            ShowHitboxes.Value = value.Hitboxes;
            CenterCamera.Value = value.CenterCamera;
            updating = false;
        }
    }

    private static bool updating;

    private void SyncSettings() {
        if (updating) return;

        CommunicationWrapper.SendSettings(StudioShared);
    }

    #region Hitboxes

    public readonly ConfigEntry<bool> ShowHitboxes;
    public readonly ConfigEntry<HitboxType> HitboxFilter;

    #endregion

    #region Round Values

    public int PositionDecimals {
        get => StudioShared.PositionDecimals;
        set {
            StudioShared.PositionDecimals = Math.Clamp(value, GameSettings.MinDecimals, GameSettings.MaxDecimals);
            GameInfo.Update();
            SyncSettings();
        }
    }

    public int SpeedDecimals {
        get => StudioShared.SpeedDecimals;
        set {
            StudioShared.SpeedDecimals = Math.Clamp(value, GameSettings.MinDecimals, GameSettings.MaxDecimals);
            GameInfo.Update();
            SyncSettings();
        }
    }

    public int VelocityDecimals {
        get => StudioShared.VelocityDecimals;
        set {
            StudioShared.VelocityDecimals = Math.Clamp(value, GameSettings.MinDecimals, GameSettings.MaxDecimals);
            GameInfo.Update();
            SyncSettings();
        }
    }

    #endregion

    #region Fast Forward

    public int FastForwardSpeed {
        get => StudioShared.FastForwardSpeed;
        set {
            StudioShared.FastForwardSpeed = Math.Clamp(value, 2, 30);
            SyncSettings();
        }
    }

    public float SlowForwardSpeed {
        get => StudioShared.SlowForwardSpeed;
        set {
            StudioShared.SlowForwardSpeed = Math.Clamp(value, 0.01f, 0.9f);
            SyncSettings();
        }
    }

    #endregion

    #region More Options

    public readonly ConfigEntry<bool> CenterCamera;

    public bool AutoPauseDraft { get; set; } = true;
    public bool AttemptConnectStudio { get; set; } = true;

    #endregion
}
