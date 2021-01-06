using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxLastFrame {
        private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;
        private static bool PlayerUpdated;

        public static void Load() {
            On.Celeste.Level.Update += LevelOnUpdate;
            On.Celeste.Player.Update += PlayerOnUpdate;
            On.Monocle.Entity.Update += EntityOnUpdate;
            On.Monocle.Hitbox.Render += HitboxOnRender;
            On.Monocle.Circle.Render += CircleOnRender;
        }

        public static void Unload() {
            On.Celeste.Level.Update -= LevelOnUpdate;
            On.Celeste.Player.Update -= PlayerOnUpdate;
            On.Monocle.Entity.Update -= EntityOnUpdate;
            On.Monocle.Hitbox.Render -= HitboxOnRender;
            On.Monocle.Circle.Render -= CircleOnRender;
        }

        // TODO Can't display the last frame hitbox when the game is paused.
        private static void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self) {
            PlayerUpdated = false;

            if (Settings.ShowHitboxes && Settings.ShowLastFrameHitboxes != LastFrameHitboxesTypes.OFF && !self.Paused) {
                foreach (Entity entity in self) {
                    if (entity.Get<PlayerCollider>() != null || entity is Platform) {
                        entity.SaveLastPosition();
                    }
                }
            }

            orig(self);
        }

        private static void PlayerOnUpdate(On.Celeste.Player.orig_Update orig, Player self) {
            orig(self);
            PlayerUpdated = true;
        }

        private static void EntityOnUpdate(On.Monocle.Entity.orig_Update orig, Entity self) {
            if (Settings.ShowHitboxes && Settings.ShowLastFrameHitboxes != LastFrameHitboxesTypes.OFF && self.Scene is Level level && !level.Paused) {
                self.SavePlayerUpdated(PlayerUpdated);
            }

            orig(self);
        }

        private static void CircleOnRender(On.Monocle.Circle.orig_Render orig, Circle self, Camera camera, Color color) {
            DrawLastFrameHitbox(self, color, HitboxColor.EntityColorInversely, hitboxColor => orig(self, camera, hitboxColor));
        }

        private static void HitboxOnRender(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
            DrawLastFrameHitbox(self, color, HitboxColor.EntityColorInversely, hitboxColor => orig(self, camera, hitboxColor));
        }

        private static void DrawLastFrameHitbox(Collider self, Color color, Color lastFrameColor, Action<Color> invokeOrig) {
            Entity entity = self.Entity;
            if (entity?.Get<PlayerCollider>() == null
                || !Settings.ShowHitboxes
                || Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.OFF
                || (entity.LoadLastPosition() == entity.Position && Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.Append)
                || !entity.UpdateLaterThanPlayer()) {
                invokeOrig(color);
                return;
            }

            // It's a bit complicated on moving platform, so display two frames of hitboxes at the same time.
            if (Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.Override
                && entity.Get<StaticMover>() is StaticMover staticMover
                && staticMover.Platform is Platform platform && platform.Scene != null
                && platform.Position != platform.LoadLastPosition()
            ) {
                invokeOrig(color);
                lastFrameColor *= 0.7f;
            }

            if (Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.Append) {
                invokeOrig(color);
                lastFrameColor *= 0.7f;
            }


            Vector2 lastPosition = entity.LoadLastPosition();
            Vector2 currentPosition = entity.Position;

            IEnumerable<PlayerCollider> playerColliders = entity.Components.GetAll<PlayerCollider>().ToArray();
            if (playerColliders.All(playerCollider => playerCollider.Collider != null)) {
                if (playerColliders.Any(playerCollider => playerCollider.Collider == self)) {
                    entity.Position = lastPosition;
                    invokeOrig(lastFrameColor);
                    entity.Position = currentPosition;
                } else {
                    invokeOrig(color);
                }
            } else {
                entity.Position = lastPosition;
                invokeOrig(lastFrameColor);
                entity.Position = currentPosition;
            }
        }
    }
}

public enum LastFrameHitboxesTypes {
    OFF,
    Override,
    Append
}